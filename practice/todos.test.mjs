import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';

const entry = fileURLToPath(new URL('./todos.mjs', import.meta.url));

test('默认按顺序列出已完成和未完成的任务', () => {
  const output = execFileSync(process.execPath, [entry], { encoding: 'utf8' });
  const [, ...items] = output.trim().split(/\r?\n/);

  assert.deepEqual(items, [
    '[x] 安装 Codex',
    '[ ] 读懂 Git 差异',
    '[ ] 完成 Skills 实战',
  ]);
});
