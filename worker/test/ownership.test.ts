import * as assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { makeFixture, PACKAGE, run, singleContext, TSCONFIG } from './fixture';

const TWO_SIX_PARAMS =
  'export function first(a: number, b: number, c: number, d: number, e: number, f: number) { return a + b + c + d + e + f; }\n' +
  "export function last(a: string, b: string, c: string, d: string, e: string, f: string) { return [f, e, d, c, b, a].join(''); }\n";

it('acceptance vector: two six-parameter functions', async () => {
  const dir = makeFixture({
    'package.json': '{"name":"fixture","version":"1.0.0","private":true}\n',
    'tsconfig.json': '{"compilerOptions":{"strict":true,"noEmit":true,"target":"ES2022"},"include":["*.ts"]}\n',
    'api.ts': TWO_SIX_PARAMS,
  });
  const result = await run(dir, { focus: ['api.ts'] });
  const context = singleContext(result);
  assert.deepEqual(context.ownedFiles, ['api.ts']);
  assert.equal(context.contextId, 'tsconfig.json');
  assert.equal(context.modulePath, 'package.json');
  const methods = context.files[0]!.methods;
  assert.deepEqual(methods.map((m) => [m.parameterCount, m.span.startLine]), [[6, 1], [6, 2]]);
  assert.deepEqual(context.diagnostics, []);
  assert.deepEqual(context.failures, []);
  assert.deepEqual(result.failures, []);
  assert.deepEqual(result.unownedFiles, []);
  assert.ok(result.inputs.some((i) => i.scope === 'repository' && i.path === 'api.ts' && /^[0-9a-f]{64}$/.test(i.sha256)));
  assert.ok(result.inputs.some((i) => i.scope === 'external' && /lib\..*d\.ts$/.test(i.path)));
});

it('a file owned by two overlapping tsconfigs belongs to both contexts', async () => {
  const dir = makeFixture({
    'package.json': PACKAGE,
    'tsconfig.json': JSON.stringify({ include: ['*.ts'] }),
    'tsconfig.strict.json': JSON.stringify({ compilerOptions: { strict: true }, include: ['a.ts'] }),
    'a.ts': 'export const a = 1;\n',
    'b.ts': 'export const b = 1;\n',
  });
  const result = await run(dir, { focus: ['a.ts'] });
  assert.deepEqual(result.contexts.map((c) => c.contextId), ['tsconfig.json', 'tsconfig.strict.json']);
  assert.deepEqual(result.contexts.map((c) => c.ownedFiles), [['a.ts', 'b.ts'], ['a.ts']]);
});

it('a config that is only extended is not a standalone context, unless referenced', async () => {
  const dir = makeFixture({
    'package.json': PACKAGE,
    'tsconfig.base.json': JSON.stringify({ compilerOptions: { strict: true }, include: ['*.ts'] }),
    'tsconfig.json': JSON.stringify({ extends: './tsconfig.base.json', include: ['*.ts'] }),
    'a.ts': 'export const a = 1;\n',
  });
  const result = await run(dir, { focus: ['a.ts'] });
  assert.deepEqual(result.contexts.map((c) => c.contextId), ['tsconfig.json']);
  const referenced = makeFixture({
    'package.json': PACKAGE,
    'tsconfig.base.json': JSON.stringify({ include: ['*.ts'] }),
    'tsconfig.json': JSON.stringify({ extends: './tsconfig.base.json', files: [], references: [{ path: './tsconfig.base.json' }] }),
    'a.ts': 'export const a = 1;\n',
  });
  const second = await run(referenced, { focus: ['a.ts'] });
  assert.ok(second.contexts.some((c) => c.contextId === 'tsconfig.base.json' && c.ownedFiles.includes('a.ts')));
});

it('a change to an extended config selects the extending context without focus files', async () => {
  const dir = makeFixture({
    'package.json': PACKAGE,
    'tsconfig.base.json': JSON.stringify({ compilerOptions: { strict: true } }),
    'tsconfig.json': JSON.stringify({ extends: './tsconfig.base.json', include: ['*.ts'] }),
    'a.ts': 'export const a = 1;\n',
  });
  const result = await run(dir, { focus: [], changed: ['tsconfig.base.json'] });
  assert.deepEqual(result.contexts.map((c) => c.contextId), ['tsconfig.json']);
  assert.deepEqual(result.contexts[0]!.files, []);
});

it('reports focus files with no owning tsconfig and a missing package.json', async () => {
  const dir = makeFixture({
    'tsconfig.json': JSON.stringify({ include: ['src/*.ts'] }),
    'src/a.ts': 'export const a = 1;\n',
    'loose.ts': 'export const l = 1;\n',
  });
  const result = await run(dir, { focus: ['src/a.ts', 'loose.ts'] });
  assert.deepEqual(result.unownedFiles, ['loose.ts']);
  const context = singleContext(result);
  assert.equal(context.modulePath, null);
  assert.deepEqual(context.failures.map((f) => f.code), ['UNRESOLVED_OWNERSHIP']);
});

it('excludes declaration files, node_modules and generated output; flags generated sources', async () => {
  const dir = makeFixture({
    'package.json': PACKAGE,
    'tsconfig.json': JSON.stringify({ compilerOptions: { outDir: 'out' }, include: ['**/*.ts'] }),
    'a.ts': 'export const a = 1;\n',
    'types.d.ts': 'export declare const t: number;\n',
    'out/a.ts': 'export const o = 1;\n',
    'node_modules/pkg/index.ts': 'export const n = 1;\n',
    'x.generated.ts': 'export const g = 1;\n',
    'y.ts': '// @generated\nexport const y = 1;\n',
  });
  const context = singleContext(await run(dir, { focus: ['a.ts', 'x.generated.ts', 'y.ts'] }));
  assert.deepEqual(context.ownedFiles, ['a.ts', 'x.generated.ts', 'y.ts']);
  assert.deepEqual(context.files.map((f) => [f.path, f.generated]), [['a.ts', false], ['x.generated.ts', true], ['y.ts', true]]);
});

it('emits types for every owned file and file code lines', async () => {
  const dir = makeFixture({
    'package.json': PACKAGE,
    'tsconfig.json': TSCONFIG,
    'a.ts': '// c\nexport interface I { x: number }\nexport abstract class A {}\n\nexport class B {}\n/* block\n comment */\n',
    'b.ts': 'export class C {}\n',
  });
  const context = singleContext(await run(dir, { focus: ['a.ts'] }));
  assert.deepEqual(
    context.types.map((t) => [t.unitId, t.isAbstract]),
    [['a.ts#A', true], ['a.ts#B', false], ['a.ts#I', true], ['b.ts#C', false]],
  );
  assert.equal(context.files[0]!.codeLines, 3);
});

it('reports a tsconfig parse failure', async () => {
  const dir = makeFixture({ 'package.json': PACKAGE, 'tsconfig.json': '{ "extends": "./missing.json" }', 'a.ts': 'export const a = 1;\n' });
  const context = singleContext(await run(dir, { focus: ['a.ts'] }));
  assert.ok(context.failures.some((f) => f.code === 'PROJECT_LOAD_FAILED' && f.ruleId === null));
});
