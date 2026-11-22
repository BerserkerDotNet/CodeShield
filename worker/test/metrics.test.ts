import * as assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { methodsFor } from './fixture';

const ifs = (count: number): string => Array.from({ length: count }, (_, i) => `if (x > ${i}) { y++; }`).join('\n');

function nested(depth: number): string {
  let source = 'y++;';
  for (let i = 0; i < depth; i++) {
    source = `if (x > ${i}) { ${source} }`;
  }
  return source;
}

it('complexity 6 (five decisions) vs 7 (six decisions)', async () => {
  const [six, seven] = await Promise.all([
    methodsFor(`export function f(x: number) { let y = 0; ${ifs(5)} return y; }`),
    methodsFor(`export function f(x: number) { let y = 0; ${ifs(6)} return y; }`),
  ]);
  assert.equal(six[0]!.complexity, 6);
  assert.equal(seven[0]!.complexity, 7);
  assert.equal(seven[0]!.decisions.length, 6);
});

it('counts loops, catch, conditional, non-default case and logical operators', async () => {
  const [m] = await methodsFor(`export function f(a: number, b?: number) {
for (let i = 0; i < a; i++) {}
for (const k of [1]) {}
for (const k in {}) {}
while (a > 1) { a--; }
do { a--; } while (a > 0);
try { a++; } catch { a--; }
const c = a ? 1 : 2;
switch (a) { case 1: break; case 2: break; default: break; }
a &&= 1; a ||= 1; b ??= 1;
return (a && b) || (a ?? b);
}`);
  const kinds = m!.decisions.map((d) => d.decisionKind);
  assert.deepEqual(kinds.filter((k) => k === 'loop').length, 5);
  assert.equal(kinds.filter((k) => k === 'catch').length, 1);
  assert.equal(kinds.filter((k) => k === 'conditional').length, 1);
  assert.equal(kinds.filter((k) => k === 'case').length, 2);
  assert.equal(kinds.filter((k) => k === 'logical').length, 4);
  assert.equal(m!.complexity, 1 + 5 + 1 + 1 + 2 + 4);
});

it('nesting 2 vs 3', async () => {
  const [two, three] = await Promise.all([
    methodsFor(`export function f(x: number) { let y = 0; ${nested(2)} return y; }`),
    methodsFor(`export function f(x: number) { let y = 0; ${nested(3)} return y; }`),
  ]);
  assert.equal(two[0]!.nesting, 2);
  assert.equal(three[0]!.nesting, 3);
  assert.deepEqual(three[0]!.nestingPoints.map((p) => p.depth), [1, 2, 3]);
});

it('else-if stays at the depth of its if; else body matches the if body', async () => {
  const [m] = await methodsFor(`export function f(x: number) {
if (x > 1) { x++; } else if (x > 2) { x--; } else if (x > 3) { x += 2; } else { x += 3; }
return x;
}`);
  assert.equal(m!.nesting, 1);
  assert.equal(m!.complexity, 4);
});

it('switch adds one level and cases add none; try/finally add none', async () => {
  const [m] = await methodsFor(`export function f(x: number) {
switch (x) { case 1: if (x) { x++; } break; default: break; }
try { x++; } finally { x--; }
return x;
}`);
  assert.equal(m!.nesting, 2);
});

it('conditional branches add a level', async () => {
  const [m] = await methodsFor('export function f(a: boolean, b: boolean, c: boolean) { return a ? (b ? 1 : 2) : c ? 3 : 4; }');
  assert.equal(m!.nesting, 2);
});

it('nested functions are separate units and do not leak decisions', async () => {
  const methods = await methodsFor(`export function outer(x: number) {
const inner = (y: number) => { if (y) { if (y > 1) { return 1; } } return x ? 1 : 2; };
return inner(x);
}`);
  const outer = methods.find((m) => m.name === 'outer')!;
  const inner = methods.find((m) => m.name === 'inner')!;
  assert.equal(outer.complexity, 1);
  assert.equal(outer.nesting, 0);
  assert.equal(inner.complexity, 4);
  assert.equal(inner.nesting, 2);
  assert.equal(inner.unitId, 'a.ts#outer.inner@2');
});

it('method length 40 vs 41 code lines, ignoring comments and blanks', async () => {
  const body = (lines: number): string => Array.from({ length: lines }, (_, i) => `  const v${i} = ${i};`).join('\n');
  const make = (inner: number): string => `export function f() { // trailing comment\n  // comment only\n\n${body(inner)}\n}`;
  const [forty, fortyOne] = await Promise.all([methodsFor(make(38)), methodsFor(make(39))]);
  assert.equal(forty[0]!.codeLines, 40);
  assert.equal(fortyOne[0]!.codeLines, 41);
});

it('parameter count 5 vs 6, including destructured, optional, rest and excluding this', async () => {
  const methods = await methodsFor(`
export function five(this: void, a: number, { b, c }: { b: number; c: number }, d?: number, ...e: number[]) {}
export function six(a: number, b: number, c: number, d: number, e: number, f: number) {}
export class K { m(this: K, a: number, [b, c]: number[]) {} }`);
  assert.equal(methods.find((m) => m.name === 'five')!.parameterCount, 4);
  assert.equal(methods.find((m) => m.name === 'six')!.parameterCount, 6);
  assert.equal(methods.find((m) => m.unitId === 'a.ts#K.m')!.parameterCount, 2);
});

it('skips overload signatures, counts constructors and accessors', async () => {
  const methods = await methodsFor(`
export function over(a: number): number;
export function over(a: string): string;
export function over(a: any): any { return a; }
export class C { private v = 1; constructor(x: number) { this.v = x; } get value() { return this.v; } set value(n: number) { this.v = n; } }`);
  assert.deepEqual(methods.map((m) => m.unitId), ['a.ts#over', 'a.ts#C.constructor', 'a.ts#C.get value', 'a.ts#C.set value']);
});
