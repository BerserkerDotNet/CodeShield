import * as assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { tokenize } from '../src/tokenize';
import { makeFixture, run, PACKAGE, TSCONFIG } from './fixture';

describe('module graph', () => {
  it('records type-only and value import edges between files', async () => {
    const dir = makeFixture({
      'package.json': PACKAGE,
      'tsconfig.json': TSCONFIG,
      'a.ts': "import type { B } from './b';\nimport { c } from './c';\nimport { type D, d } from './d';\nexport const a: B | D = c + d;\n",
      'b.ts': 'export type B = number;\n',
      'c.ts': 'export const c = 1;\n',
      'd.ts': 'export type D = number;\nexport const d = 1;\n',
    });
    const { graph } = await run(dir);
    const byTarget = Object.fromEntries(graph.fileImports.filter((e) => e.from === 'a.ts').map((e) => [e.to, e.typeOnly]));
    assert.deepEqual(byTarget, { 'b.ts': true, 'c.ts': false, 'd.ts': false });
  });

  it('records package edges between nearest package.json files', async () => {
    const dir = makeFixture({
      'package.json': PACKAGE,
      'tsconfig.json': JSON.stringify({ include: ['**/*.ts'] }),
      'app/package.json': PACKAGE,
      'app/main.ts': "import { lib } from '../lib/index';\nexport const m = lib;\n",
      'lib/package.json': PACKAGE,
      'lib/index.ts': 'export const lib = 1;\n',
    });
    const { graph } = await run(dir);
    assert.deepEqual(graph.modules, ['app/package.json', 'lib/package.json', 'package.json']);
    assert.deepEqual(graph.edges, [{ from: 'app/package.json', to: 'lib/package.json', typeOnly: false }]);
  });

  it('reports unresolved relative imports', async () => {
    const dir = makeFixture({ 'package.json': PACKAGE, 'tsconfig.json': TSCONFIG, 'a.ts': "import './nope';\nexport {};\n" });
    const { graph } = await run(dir);
    assert.equal(graph.unresolved.length, 1);
  });
});

describe('tokenization', () => {
  it('emits scanner-accurate tokens without comments', () => {
    const dir = makeFixture({ 'a.ts': "// c\nconst s = `x${1}y`; const r = /a+/g; /* b */\n" });
    const [file] = tokenize({ protocol: 1, command: 'tokenize', root: dir, files: ['a.ts'] }).files;
    const texts = file!.tokens.map((t) => t.text);
    assert.ok(!texts.some((t) => t.includes('//') || t.includes('/* b */')));
    assert.ok(texts.includes('/a+/g'));
    assert.deepEqual(file!.tokens[0], {
      kind: 'ConstKeyword', text: 'const', line: 2, column: 1, endLine: 2, endColumn: 6, offset: 5,
    });
    assert.ok(texts.includes('`x${') && texts.includes('}y`'));
  });

  it('reports unreadable files', () => {
    const dir = makeFixture({});
    const result = tokenize({ protocol: 1, command: 'tokenize', root: dir, files: ['missing.ts'] });
    assert.equal(result.failures[0]!.code, 'UNREADABLE_SOURCE');
  });
});
