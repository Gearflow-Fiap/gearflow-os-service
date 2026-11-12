"""Configure only this service's secrets without logging sensitive values."""
import base64
import json
import os
import subprocess

SERVICE = 'os'
def kubectl(*args, payload=None):
    result = subprocess.run(['kubectl', *args], input=json.dumps(payload) if payload else None,
                            text=True, capture_output=True)
    if result.returncode:
        raise SystemExit('Kubernetes configuration failed; check permissions and prerequisite resources.')
    return json.loads(result.stdout) if result.stdout.strip() else None

def secret(name, values):
    kubectl('apply', '-f', '-', '-o', 'json', payload={
        'apiVersion': 'v1', 'kind': 'Secret', 'type': 'Opaque',
        'metadata': {'name': name, 'namespace': 'gearflow'},
        'data': {k: base64.b64encode(v.encode()).decode() for k, v in values.items()}})

def main():
    password, jwt = os.environ['DB_PASSWORD'], os.environ['JWT_SIGNING_KEY']
    if not password or len(jwt.encode()) < 32:
        raise SystemExit('DB_PASSWORD and JWT_SIGNING_KEY (at least 32 UTF-8 bytes) are required.')
    source = kubectl('get', 'secret', os.environ['BROKER_SECRET'], '-n', 'messaging', '-o', 'json')['data']
    user = base64.b64decode(source['username'], validate=True).decode()
    broker_password = base64.b64decode(source['password'], validate=True).decode()
    if user != os.environ['BROKER_USER'] or not broker_password:
        raise SystemExit('Broker credential does not match the expected service user.')
    # A quoted Npgsql value preserves semicolons and quotes in the configured password.
    quoted = '"' + password.replace('"', '""') + '"'
    connection = f'Host={SERVICE}-postgres;Port=5432;Database=gearflow_{SERVICE};Username={SERVICE};Password={quoted};GSS Encryption Mode=Disable'
    secret(f'gearflow-{SERVICE}-postgres', {'POSTGRES_DB': f'gearflow_{SERVICE}', 'POSTGRES_USER': SERVICE, 'POSTGRES_PASSWORD': password})
    values = {'ConnectionStrings__OsDb': connection, 'Jwt__Secret': jwt,
              'Jwt__Issuer': 'GearFlow.Api', 'Jwt__Audience': 'GearFlow.Client'}
    if SERVICE == 'operation':
        values.update({'RabbitMq__Username': user, 'RabbitMq__Password': broker_password})
        secret('gearflow-operation-migrations', {'ConnectionStrings__Operation': connection})
    else:
        secret('rabbitmq-os-credentials', {'username': user, 'password': broker_password})
    secret(f'gearflow-{SERVICE}-secret', values)
    print('Application, database and individual broker credentials configured.')

if __name__ == '__main__':
    main()
