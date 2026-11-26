#!/usr/bin/env bash
set -euo pipefail

# Run all tests with .NET 8 and an ephemeral CA, without changing host trust.
if [[ "${1:-}" != "--inside-container" ]]; then
  repo_root="$(cd "$(dirname "$0")/.." && pwd)"
  exec docker run --rm \
    -v "${repo_root}:/source:ro" \
    --mount type=volume,src=bsource-notifier-test-nuget,dst=/root/.nuget/packages \
    --entrypoint bash mcr.microsoft.com/dotnet/sdk:8.0 \
    /source/tests/run-tests.sh --inside-container
fi

mkdir -p /workspace
# Do not copy developer secrets or host build outputs into the test workspace.
tar -C /source --exclude=.git --exclude=bin --exclude=obj --exclude=TestResults \
  --exclude=.env.local --exclude=appsettings.Development.json -cf - . | tar -C /workspace -xf -
cd /workspace
cert_dir="$(mktemp -d)"
trap 'rm -rf "$cert_dir"' EXIT
openssl req -x509 -newkey rsa:2048 -nodes -days 1 -sha256 \
  -subj '/CN=Notifier SMTP test CA' \
  -addext 'basicConstraints=critical,CA:TRUE' \
  -keyout "$cert_dir/ca.key" -out "$cert_dir/ca.crt" 2>/dev/null
for cert_name in localhost wronghost; do
  openssl req -newkey rsa:2048 -nodes -subj "/CN=${cert_name}" \
    -keyout "$cert_dir/${cert_name}.key" -out "$cert_dir/${cert_name}.csr" 2>/dev/null
  printf 'subjectAltName=DNS:%s\nextendedKeyUsage=serverAuth\nbasicConstraints=critical,CA:FALSE\n' "$cert_name" > "$cert_dir/${cert_name}.ext"
  openssl x509 -req -days 1 -sha256 -in "$cert_dir/${cert_name}.csr" \
    -CA "$cert_dir/ca.crt" -CAkey "$cert_dir/ca.key" -CAcreateserial \
    -extfile "$cert_dir/${cert_name}.ext" -out "$cert_dir/${cert_name}.crt" 2>/dev/null
  openssl pkcs12 -export -out "$cert_dir/${cert_name}.pfx" \
    -inkey "$cert_dir/${cert_name}.key" -in "$cert_dir/${cert_name}.crt" -passout pass:
done
openssl req -x509 -newkey rsa:2048 -nodes -days 1 -sha256 \
  -subj '/CN=localhost' -addext 'subjectAltName=DNS:localhost' \
  -keyout "$cert_dir/untrusted.key" -out "$cert_dir/untrusted.crt" 2>/dev/null
openssl pkcs12 -export -out "$cert_dir/untrusted.pfx" \
  -inkey "$cert_dir/untrusted.key" -in "$cert_dir/untrusted.crt" -passout pass:
# Trust only inside this disposable container; production validation remains intact.
cp "$cert_dir/ca.crt" /usr/local/share/ca-certificates/notifier-smtp-test.crt
update-ca-certificates >/dev/null 2>&1
export SMTP_TEST_CERT_DIR="$cert_dir"
dotnet test BSourceNotifier.sln --configuration Release --logger 'console;verbosity=normal'
