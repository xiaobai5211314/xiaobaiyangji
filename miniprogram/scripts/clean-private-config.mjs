import { rmSync } from 'node:fs';
import { resolve } from 'node:path';

const privateConfigPaths = [
  'dist/build/mp-weixin/project.private.config.json',
  'dist/dev/mp-weixin/project.private.config.json'
];

for (const relativePath of privateConfigPaths) {
  const absolutePath = resolve(process.cwd(), relativePath);
  rmSync(absolutePath, { force: true });
  console.log(`[clean:private-config] removed if present: ${relativePath}`);
}
