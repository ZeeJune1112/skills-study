import { readFileSync } from 'node:fs';

const todos = JSON.parse(readFileSync(new URL('./todos.json', import.meta.url), 'utf8'));

console.log('待办清单');
for (const todo of todos) {
  const mark = todo.completed ? 'x' : ' ';
  console.log(`[${mark}] ${todo.title}`);
}
