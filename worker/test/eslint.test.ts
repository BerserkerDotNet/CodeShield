import * as assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { CORRECTNESS_RULES, makeFixture, PACKAGE, run, singleContext } from './fixture';

const TSCONFIG_STRICT = JSON.stringify({ compilerOptions: { strict: true, noEmit: true, target: 'ES2022' }, include: ['*.ts'] });

const TRIGGERS: Record<string, string> = {
  'no-floating-promises': 'async function f() {}\nf();\nvoid f();\n',
  'no-misused-promises': 'if (Promise.resolve(true)) { }\n',
  'await-thenable': 'async function g() { await 1; }\n',
  'array-callback-return': '[1].map((x) => { x; });\n',
  'for-direction': 'for (let i = 0; i < 10; i--) { }\n',
  'no-async-promise-executor': 'new Promise(async (r) => r(1));\n',
  'no-constant-binary-expression': 'const a = {} ?? 1;\n',
  'no-dupe-else-if': 'declare const p: boolean;\nif (p) { } else if (p) { }\n',
  'no-self-assign': 'let s = 1;\ns = s;\n',
  'no-unsafe-finally': 'function h() { try { } finally { return 1; } }\n',
  'no-unsafe-optional-chaining': 'declare const o: { c: { b: number } } | undefined;\nconst { b } = o?.c;\n',
  'use-isnan': 'declare const n: number;\nconst t = n === NaN;\n',
};

const files: Record<string, string> = { 'package.json': PACKAGE, 'tsconfig.json': TSCONFIG_STRICT, 'clean.ts': 'export const ok = 1;\n' };
for (const [rule, source] of Object.entries(TRIGGERS)) {
  files[`${rule}.ts`] = source;
}
const dir = makeFixture(files);
const resultPromise = run(dir, { rules: CORRECTNESS_RULES });

for (const rule of Object.keys(TRIGGERS)) {
  it(`${rule} triggers on its example only`, async () => {
    const context = singleContext(await resultPromise);
    assert.deepEqual(context.failures, []);
    const hits = context.diagnostics.filter((d) => d.analyzerId.endsWith(rule) || d.analyzerId === rule);
    assert.ok(hits.length >= 1, `no diagnostic for ${rule}`);
    assert.ok(hits.every((d) => d.file === `${rule}.ts`) || rule === 'no-floating-promises' || rule === 'no-misused-promises');
    assert.ok(!context.diagnostics.some((d) => d.file === 'clean.ts'));
  });
}

it('no-floating-promises flags void with ignoreVoid false', async () => {
  const context = singleContext(await resultPromise);
  const lines = context.diagnostics.filter((d) => d.file === 'no-floating-promises.ts').map((d) => d.span.startLine);
  assert.deepEqual(lines, [2, 3]);
});

it('eslint-disable comments are SUPPRESSED_RULE failures', async () => {
  const suppressed = makeFixture({
    'package.json': PACKAGE,
    'tsconfig.json': TSCONFIG_STRICT,
    'a.ts': '// eslint-disable-next-line use-isnan\nexport const t = 1 === NaN;\n',
    'b.ts': '/* eslint-disable */\nexport const u = 1;\n',
  });
  const context = singleContext(await run(suppressed, { rules: CORRECTNESS_RULES }));
  const suppressedFailures = context.failures.filter((f) => f.code === 'SUPPRESSED_RULE');
  assert.equal(suppressedFailures.length, 13);
  assert.equal(suppressedFailures.filter((f) => f.message.includes('a.ts')).length, 1);
  assert.equal(suppressedFailures.filter((f) => f.message.includes('b.ts')).length, 12);
  assert.ok(context.diagnostics.some((d) => d.analyzerId === 'use-isnan'));
});

it('unknown analyzers fail with MISSING_ANALYZER', async () => {
  const dir = makeFixture({ 'package.json': PACKAGE, 'tsconfig.json': TSCONFIG_STRICT, 'a.ts': 'export const a = 1;\n' });
  const context = singleContext(
    await run(dir, { rules: [{ analyzerId: 'no-such-rule', evaluatorId: 'x', options: [] }] }),
  );
  assert.equal(context.failures[0]!.code, 'MISSING_ANALYZER');
});
