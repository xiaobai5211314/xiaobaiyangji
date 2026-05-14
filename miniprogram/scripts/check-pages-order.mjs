import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';

const expectedFirstPage = 'pages/home/index';
const files = [
  'src/pages.json',
  'dist/dev/mp-weixin/app.json',
  'dist/build/mp-weixin/app.json'
];

function readJson(relativePath) {
  const filePath = resolve(relativePath);
  if (!existsSync(filePath)) {
    throw new Error(`${relativePath} 不存在，请先运行对应构建命令。`);
  }

  return JSON.parse(stripJsonComments(readFileSync(filePath, 'utf8')));
}

function stripJsonComments(text) {
  let result = '';
  let inString = false;
  let quote = '';
  let escaped = false;

  for (let index = 0; index < text.length; index += 1) {
    const char = text[index];
    const next = text[index + 1];

    if (inString) {
      result += char;
      if (escaped) {
        escaped = false;
      } else if (char === '\\') {
        escaped = true;
      } else if (char === quote) {
        inString = false;
      }
      continue;
    }

    if (char === '"' || char === "'") {
      inString = true;
      quote = char;
      result += char;
      continue;
    }

    if (char === '/' && next === '/') {
      while (index < text.length && text[index] !== '\n') index += 1;
      result += '\n';
      continue;
    }

    result += char;
  }

  return result;
}

function firstPageOf(filePath, json) {
  const first = json.pages?.[0];
  if (typeof first === 'string') return first;
  return first?.path;
}

for (const filePath of files) {
  const json = readJson(filePath);
  const firstPage = firstPageOf(filePath, json);
  if (firstPage !== expectedFirstPage) {
    throw new Error(`${filePath} pages[0] 是 ${firstPage || '空'}，必须是 ${expectedFirstPage}`);
  }

  console.log(`[pages-order] ${filePath}: ${firstPage}`);
}
