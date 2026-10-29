import { spawn, spawnSync } from 'node:child_process';
import { mkdirSync } from 'node:fs';
import { join } from 'node:path';

// Use the trusted .NET localhost certificate for a stable OAuth callback origin.
const directory = join(process.cwd(), '.certificates');
mkdirSync(directory, { recursive: true });
const certificate = join(directory, 'localhost.pem');
const result = spawnSync('dotnet', [
  'dev-certs', 'https', '--export-path', certificate, '--format', 'Pem', '--no-password',
], { stdio: 'inherit' });
if (result.error) throw result.error;
if (result.status !== 0) {
  throw new Error('Unable to export the development certificate. Run dotnet dev-certs https --trust.');
}
const server = spawn(process.execPath, [
  'node_modules/@angular/cli/bin/ng.js', 'serve', '--host', 'localhost',
  '--port', process.env.PORT ?? '4200', '--ssl',
  '--ssl-cert', certificate, '--ssl-key', join(directory, 'localhost.key'),
], { stdio: 'inherit' });
server.on('error', error => { console.error(error); process.exitCode = 1; });
server.on('exit', code => { process.exitCode = code ?? 1; });
for (const signal of ['SIGINT', 'SIGTERM']) {
  process.on(signal, () => server.kill(signal));
}
