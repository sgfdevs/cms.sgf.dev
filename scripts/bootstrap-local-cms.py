#!/usr/bin/env python3
"""Create a guarded local Umbraco bootstrap config and run the CMS."""

from __future__ import annotations

import argparse
import base64
import copy
import hashlib
import hmac
import json
import os
import secrets
import shutil
import stat
import subprocess
import sys
from pathlib import Path

CONFIG_ENV_VAR = "SGFDEVS_LOCAL_BOOTSTRAP_CONFIG_PATH"
DEFAULT_PORT = 5099
CONFIG_FILE_NAME = "local-bootstrap.appsettings.json"
DB_FILE_NAME = "local-bootstrap.sqlite"
CREATED_BY = "scripts/bootstrap-local-cms.py"
LOCAL_S3_ENDPOINT = "http://127.0.0.1:8333"
LOCAL_S3_BUCKET = "sgf-dev-local"
LOCAL_S3_ACCESS_KEY = "sgf-dev-local"
LOCAL_S3_SECRET_KEY = "sgf-dev-local-password"
POLICY_VERSION = 1
DOCKER_CONTEXT_NAME = "default"
DOCKER_PROJECT_NAME = "sgf-dev-local"
FORBIDDEN_PARENT_DOCKER_ENV = ("DOCKER_HOST", "DOCKER_CONTEXT")
DOCKER_ENV_PREFIXES_TO_CLEAR = ("COMPOSE_",)
PROXY_ENV_VARS = ("HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "http_proxy", "https_proxy", "all_proxy")
TELEMETRY_ENV_VARS = (
    "SENTRY_DSN",
    "Sentry__Dsn",
    "SENTRY__DSN",
    "OTEL_EXPORTER_OTLP_ENDPOINT",
    "OTEL_EXPORTER_OTLP_TRACES_ENDPOINT",
    "OTEL_EXPORTER_OTLP_METRICS_ENDPOINT",
    "OTEL_EXPORTER_OTLP_LOGS_ENDPOINT",
    "OTEL_EXPORTER_OTLP_PROTOCOL",
    "OTEL_EXPORTER_OTLP_TRACES_PROTOCOL",
    "OTEL_EXPORTER_OTLP_METRICS_PROTOCOL",
    "OTEL_EXPORTER_OTLP_LOGS_PROTOCOL",
    "OTEL_EXPORTER_OTLP_HEADERS",
    "OTEL_EXPORTER_OTLP_TRACES_HEADERS",
    "OTEL_EXPORTER_OTLP_METRICS_HEADERS",
    "OTEL_EXPORTER_OTLP_LOGS_HEADERS",
    "OTEL_TRACES_EXPORTER",
    "OTEL_METRICS_EXPORTER",
    "OTEL_LOGS_EXPORTER",
    "OpenTelemetry__Exporter__Otlp__Endpoint",
    "OpenTelemetry__Exporter__Otlp__Protocol",
    "OpenTelemetry__Exporter__Otlp__Headers",
)
DOTNET_INSTRUMENTATION_ENV_VARS = (
    "DOTNET_STARTUP_HOOKS",
    "DOTNET_ADDITIONAL_DEPS",
    "DOTNET_SHARED_STORE",
    "CORECLR_ENABLE_PROFILING",
    "CORECLR_PROFILER",
    "CORECLR_PROFILER_PATH",
    "CORECLR_PROFILER_PATH_64",
    "CORECLR_PROFILER_PATH_32",
    "COR_ENABLE_PROFILING",
    "COR_PROFILER",
    "COR_PROFILER_PATH",
    "COR_PROFILER_PATH_64",
    "COR_PROFILER_PATH_32",
)


class BootstrapError(RuntimeError):
    pass


def main() -> int:
    parser = argparse.ArgumentParser(description="Run the SGF CMS against a disposable local bootstrap database.")
    parser.add_argument("--repo-root", default=Path.cwd(), type=Path, help="cms.sgf.dev repo root. Defaults to the current directory.")
    parser.add_argument("--port", default=DEFAULT_PORT, type=int, help=f"Loopback CMS port. Defaults to {DEFAULT_PORT}.")
    args = parser.parse_args()

    try:
        repo_root = require_repo(args.repo_root)
        require_development_environment(os.environ)
        require_loopback_port(args.port)

        app_root = repo_root / "SgfDevs"
        bootstrap_dir = app_root / "umbraco" / "Data" / "local-bootstrap"
        database_path = bootstrap_dir / DB_FILE_NAME
        config_path = bootstrap_dir / CONFIG_FILE_NAME

        validate_bootstrap_paths(app_root, bootstrap_dir, database_path, config_path)
        require_executable("docker")
        require_executable("dotnet")
        docker_env = build_docker_environment(os.environ)
        verify_local_docker_context(docker_env)
        ensure_bootstrap_files(bootstrap_dir, database_path, config_path)

        print(f"Local bootstrap config: {config_path}")
        print("Admin credentials were written to that private gitignored file. The password is not printed.")
        print(f"Local bootstrap database: {database_path}")
        compose_file = repo_root / "compose.yaml"
        print(f"Starting SeaweedFS service from {compose_file} without deleting volumes.")
        run_checked(
            [
                "docker",
                "--context",
                DOCKER_CONTEXT_NAME,
                "compose",
                "-f",
                str(compose_file),
                "--project-name",
                DOCKER_PROJECT_NAME,
                "up",
                "-d",
                "seaweedfs",
            ],
            cwd=repo_root,
            env=docker_env,
        )

        child_env = build_child_environment(os.environ, config_path, args.port)
        print(f"Starting CMS on http://127.0.0.1:{args.port} with --no-launch-profile. Stop it with Ctrl+C.")
        return subprocess.call(
            [
                "dotnet",
                "run",
                "--project",
                str(app_root / "SgfDevs.csproj"),
                "--no-launch-profile",
                "--",
                "--urls",
                f"http://127.0.0.1:{args.port}",
            ],
            cwd=app_root,
            env=child_env,
        )
    except BootstrapError as error:
        print(f"error: {error}", file=sys.stderr)
        return 2


def require_repo(repo_root: Path) -> Path:
    root = repo_root.resolve(strict=True)
    required_files = [
        root / "SgfDevs" / "SgfDevs.csproj",
        root / "SgfDevs" / "Program.cs",
        root / "SgfDevs" / "Dev" / "LocalBootstrap" / "LocalBootstrapGuard.cs",
        root / "compose.yaml",
    ]
    missing = [str(path) for path in required_files if not path.is_file()]
    if missing:
        raise BootstrapError("not an SGF CMS repo root, missing " + ", ".join(missing))
    return root


def require_development_environment(env: os._Environ[str]) -> None:
    explicit_values = {
        name: value.strip()
        for name in ("ASPNETCORE_ENVIRONMENT", "DOTNET_ENVIRONMENT")
        if (value := env.get(name)) is not None and value.strip()
    }
    for name, value in explicit_values.items():
        if value.lower() != "development":
            raise BootstrapError(f"{name} is {value!r}; local bootstrap only runs with Development or with no environment set.")


def require_loopback_port(port: int) -> None:
    if port < 1024 or port > 65535:
        raise BootstrapError("port must be between 1024 and 65535")


def validate_bootstrap_paths(app_root: Path, bootstrap_dir: Path, database_path: Path, config_path: Path) -> None:
    app_root_resolved = app_root.resolve(strict=True)
    if path_has_symlink(app_root_resolved, bootstrap_dir):
        raise BootstrapError("bootstrap directory path contains a symbolic link")
    if bootstrap_dir.exists() and not bootstrap_dir.is_dir():
        raise BootstrapError(f"bootstrap path exists but is not a directory: {bootstrap_dir}")

    allowed_parent = (app_root_resolved / "umbraco" / "Data" / "local-bootstrap").resolve(strict=False)
    for path in (database_path, config_path):
        resolved = path.resolve(strict=False)
        if not is_relative_to(resolved, allowed_parent):
            raise BootstrapError(f"refusing path outside local bootstrap directory: {path}")
        if path.name not in {DB_FILE_NAME, CONFIG_FILE_NAME}:
            raise BootstrapError(f"unexpected bootstrap file name: {path.name}")
        if path.exists() and path.is_symlink():
            raise BootstrapError(f"refusing symbolic link file: {path}")


def ensure_bootstrap_files(bootstrap_dir: Path, database_path: Path, config_path: Path) -> None:
    old_umask = os.umask(0o077)
    try:
        if not bootstrap_dir.exists():
            bootstrap_dir.mkdir(mode=0o700, parents=True)

        if config_path.exists():
            validate_existing_config(config_path, database_path)
            os.chmod(bootstrap_dir, 0o700)
        elif database_path.exists():
            raise BootstrapError(
                "local bootstrap database exists without the private config file that proves this script owns it"
            )
        else:
            write_new_config(config_path, database_path)
            os.chmod(bootstrap_dir, 0o700)

        if database_path.exists() and database_path.is_symlink():
            raise BootstrapError("local bootstrap database cannot be a symbolic link")
    finally:
        os.umask(old_umask)


def validate_existing_config(config_path: Path, database_path: Path) -> None:
    mode = stat.S_IMODE(config_path.stat().st_mode)
    if mode & 0o077:
        raise BootstrapError(f"private config must not be group/world readable: {config_path}")

    try:
        with config_path.open("r", encoding="utf-8") as file:
            config = json.load(file)
    except json.JSONDecodeError as error:
        raise BootstrapError(f"private config is not valid JSON: {error}") from error

    validate_generated_policy(config, database_path)


def validate_generated_policy(config: object, database_path: Path) -> None:
    # This signature detects drift in a generated file. It is not a boundary against
    # the same OS user editing this script, the config, or both.
    if not isinstance(config, dict):
        raise BootstrapError("private config must be a JSON object")

    password = read_nested_string(config, ["Umbraco", "CMS", "Unattended", "UnattendedUserPassword"])
    hmac_secret = read_nested_string(config, ["Umbraco", "CMS", "Imaging", "HMACSecretKey"])
    signature = read_nested_string(config, ["SGFDevs", "LocalBootstrap", "PolicySignature"])
    if len(password) < 30:
        raise BootstrapError("private config must keep the generated local admin password")
    if not hmac_secret:
        raise BootstrapError("private config must keep the generated local imaging HMAC secret")
    if not signature:
        raise BootstrapError("private config is missing its local bootstrap policy signature")

    unsigned_config = copy.deepcopy(config)
    unsigned_config["SGFDevs"]["LocalBootstrap"].pop("PolicySignature", None)
    expected_signature = sign_policy(unsigned_config, hmac_secret)
    if not hmac.compare_digest(signature, expected_signature):
        raise BootstrapError("private config local bootstrap policy signature does not match")

    expected = build_config(database_path, password, hmac_secret)
    expected["SGFDevs"]["LocalBootstrap"]["PolicySignature"] = signature
    if config != expected:
        raise BootstrapError("private config no longer matches the generated local bootstrap safety policy")


def read_nested_string(value: object, keys: list[str]) -> str:
    current = value
    for key in keys:
        if not isinstance(current, dict):
            return ""
        current = current.get(key)
    return current if isinstance(current, str) else ""


def build_config(database_path: Path, password: str, hmac_secret: str) -> dict[str, object]:
    return {
        "SGFDevs": {
            "EventSyncEnabled": False,
            "NewsletterEndpoint": "",
            "NewsletterListId": "",
            "LocalBootstrap": {
                "Enabled": True,
                "SeedFictionalContent": False,
                "CreatedBy": CREATED_BY,
                "PolicyVersion": POLICY_VERSION,
            },
            "Sessionize": {"BaseUrl": ""},
            "MeetupApi": {"BaseUrl": "", "ClientId": "", "ClientSecret": ""},
            "Site": {"AnalyticsEnabled": False, "SearchIndexingEnabled": False},
        },
        "ConnectionStrings": {
            "umbracoDbDSN": f"Data Source={database_path};Cache=Shared",
            "umbracoDbDSN_ProviderName": "Microsoft.Data.Sqlite",
        },
        "AWS": {
            "Region": "us-east-2",
            "ServiceURL": LOCAL_S3_ENDPOINT,
            "ForcePathStyle": True,
        },
        "Umbraco": {
            "Storage": {
                "AWSS3": {
                    "Media": {
                        "BucketName": LOCAL_S3_BUCKET,
                        "Region": "us-east-2",
                        "MediaBucketPrefix": "media",
                        "CacheBucketPrefix": "cache",
                        "CacheRetention": {"Enabled": False},
                    }
                }
            },
            "CMS": {
                "Imaging": {
                    "HMACSecretKey": hmac_secret,
                },
                "Unattended": {
                    "InstallUnattended": True,
                    "UpgradeUnattended": True,
                    "PackageMigrationsUnattended": True,
                    "UnattendedUserName": "SGF Local Bootstrap Admin",
                    "UnattendedUserEmail": "local-bootstrap-admin@sgf.dev.invalid",
                    "UnattendedUserPassword": password,
                    "UnattendedTelemetryLevel": "Minimal",
                },
            },
        },
        "uSync": {
            "Settings": {
                "ImportOnFirstBoot": False,
                "ImportAtStartup": "None",
                "ExportAtStartup": "None",
                "ExportOnSave": "None",
            }
        },
    }


def sign_policy(config: dict[str, object], hmac_secret: str) -> str:
    payload = json.dumps(config, sort_keys=True, separators=(",", ":")).encode("utf-8")
    return hmac.new(hmac_secret.encode("utf-8"), payload, hashlib.sha256).hexdigest()


def write_new_config(config_path: Path, database_path: Path) -> None:
    password = secrets.token_urlsafe(30)
    hmac_secret = base64.b64encode(secrets.token_bytes(64)).decode("ascii")
    config = build_config(database_path, password, hmac_secret)
    config["SGFDevs"]["LocalBootstrap"]["PolicySignature"] = sign_policy(config, hmac_secret)

    flags = os.O_WRONLY | os.O_CREAT | os.O_EXCL
    if hasattr(os, "O_NOFOLLOW"):
        flags |= os.O_NOFOLLOW
    fd = os.open(config_path, flags, 0o600)
    with os.fdopen(fd, "w", encoding="utf-8") as file:
        json.dump(config, file, indent=2)
        file.write("\n")


def build_child_environment(parent_env: os._Environ[str], config_path: Path, port: int) -> dict[str, str]:
    env = dict(parent_env)
    clear_child_only_environment(env)
    env.update(
        {
            "ASPNETCORE_ENVIRONMENT": "Development",
            "DOTNET_ENVIRONMENT": "Development",
            "ASPNETCORE_URLS": f"http://127.0.0.1:{port}",
            CONFIG_ENV_VAR: str(config_path),
            "AWS_ACCESS_KEY_ID": LOCAL_S3_ACCESS_KEY,
            "AWS_SECRET_ACCESS_KEY": LOCAL_S3_SECRET_KEY,
            "AWS_SESSION_TOKEN": "",
            "AWS_PROFILE": "",
            "AWS_DEFAULT_PROFILE": "",
            "AWS_EC2_METADATA_DISABLED": "true",
            "NO_PROXY": "127.0.0.1,localhost,::1",
            "no_proxy": "127.0.0.1,localhost,::1",
        }
    )
    return env


def build_docker_environment(parent_env: os._Environ[str]) -> dict[str, str]:
    for name in FORBIDDEN_PARENT_DOCKER_ENV:
        if parent_env.get(name):
            raise BootstrapError(f"{name} is set; local bootstrap only uses the local Docker context")

    env = dict(parent_env)
    for name in list(env):
        if name in FORBIDDEN_PARENT_DOCKER_ENV or name in PROXY_ENV_VARS or any(name.startswith(prefix) for prefix in DOCKER_ENV_PREFIXES_TO_CLEAR):
            env.pop(name, None)
    return env


def verify_local_docker_context(env: dict[str, str]) -> None:
    completed = subprocess.run(
        ["docker", "context", "inspect", DOCKER_CONTEXT_NAME, "--format", "{{json .Endpoints.docker.Host}}"],
        check=False,
        cwd=Path.cwd(),
        env=env,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
    )
    if completed.returncode != 0:
        raise BootstrapError(f"could not inspect local Docker context {DOCKER_CONTEXT_NAME!r}")

    endpoint = parse_docker_context_endpoint(completed.stdout)
    if not is_local_docker_endpoint(endpoint):
        raise BootstrapError(f"Docker context {DOCKER_CONTEXT_NAME!r} is not a local endpoint")


def parse_docker_context_endpoint(output: str) -> str:
    value = output.strip()
    if not value:
        raise BootstrapError(f"Docker context {DOCKER_CONTEXT_NAME!r} did not report a Docker endpoint")
    try:
        parsed = json.loads(value)
    except json.JSONDecodeError:
        parsed = value.strip('"')
    if not isinstance(parsed, str) or not parsed.strip():
        raise BootstrapError(f"Docker context {DOCKER_CONTEXT_NAME!r} did not report a Docker endpoint")
    return parsed.strip()


def is_local_docker_endpoint(endpoint: str) -> bool:
    normalized = endpoint.lower()
    return normalized.startswith("unix://") or normalized.startswith("npipe://")


def clear_child_only_environment(env: dict[str, str]) -> None:
    for name in list(env):
        if name.startswith("OTEL_"):
            env.pop(name, None)
    for name in PROXY_ENV_VARS + TELEMETRY_ENV_VARS + DOTNET_INSTRUMENTATION_ENV_VARS:
        env.pop(name, None)


def require_executable(name: str) -> None:
    if shutil.which(name) is None:
        raise BootstrapError(f"required executable not found on PATH: {name}")


def run_checked(command: list[str], cwd: Path, env: dict[str, str]) -> None:
    require_executable(command[0])
    completed = subprocess.run(command, cwd=cwd, env=env, check=False)
    if completed.returncode != 0:
        raise BootstrapError(f"command failed with exit code {completed.returncode}: {' '.join(command)}")


def path_has_symlink(root: Path, target: Path) -> bool:
    current = target
    root = root.resolve(strict=True)
    while True:
        if current.exists() and current.is_symlink():
            return True
        if current == root or current.parent == current:
            return False
        current = current.parent


def is_relative_to(path: Path, parent: Path) -> bool:
    try:
        path.relative_to(parent)
        return True
    except ValueError:
        return False


if __name__ == "__main__":
    raise SystemExit(main())
