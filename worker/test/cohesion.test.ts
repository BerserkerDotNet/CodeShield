import * as assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { makeFixture, PACKAGE, run, singleContext, TSCONFIG } from './fixture';
import type { ClassFact } from '../src/protocol';

async function classesFor(source: string): Promise<ClassFact[]> {
  const dir = makeFixture({ 'package.json': PACKAGE, 'tsconfig.json': TSCONFIG, 'a.ts': source });
  return singleContext(await run(dir)).classes;
}

it('one component when methods share a field or call each other', async () => {
  const [c] = await classesFor(`export class A {
private left = 0; private right = 0;
inc() { this.left++; }
total() { return this.left + this.right; }
reset() { this.inc(); }
other() { return this.right; }
}`);
  assert.equal(c!.applicable, true);
  assert.equal(c!.components.length, 1);
  assert.ok(c!.edges.some((e) => e.reason === 'call'));
  assert.ok(c!.edges.some((e) => e.reason === 'field:left'));
});

it('two components for disjoint method groups, including parameter properties', async () => {
  const [c] = await classesFor(`export class B {
private a = 0;
constructor(private readonly b: number) {}
nextA() { return ++this.a; }
readB() { return this.b; }
}`);
  assert.equal(c!.applicable, true);
  assert.deepEqual(c!.components, [['a.ts#B.nextA'], ['a.ts#B.readB']]);
  assert.deepEqual(c!.nodes.map((n) => n.name), ['nextA', 'readB']);
});

it('is inapplicable without instance state or with fewer than two nodes', async () => {
  const classes = await classesFor(`
export class Stateless { a() { return 1; } b() { return 2; } }
export class Single { private x = 1; only() { return this.x; } }
export class StaticOnly { static s = 1; private x = 1; static a() { return StaticOnly.s; } b() { return this.x; } }`);
  assert.equal(classes.length, 3);
  assert.ok(classes.every((c) => !c.applicable && c.components.length === 0));
});

it('ignores constructors, static members, nested functions and inherited fields', async () => {
  const [, derived] = await classesFor(`
class Base { protected shared = 1; }
export class D extends Base {
private own = 0;
constructor() { super(); this.own = 1; }
a() { return this.shared; }
b() { const f = () => this.own; return f(); }
c() { return this.own; }
}`);
  assert.equal(derived!.applicable, true);
  assert.deepEqual(derived!.components, [['a.ts#D.a'], ['a.ts#D.b'], ['a.ts#D.c']]);
});

it('treats accessors as nodes connected through their shared field', async () => {
  const [c] = await classesFor(`export class E {
private v = 0;
get value() { return this.v; }
set value(n: number) { this.v = n; }
other() { return 1; }
}`);
  assert.deepEqual(c!.components, [['a.ts#E.get value', 'a.ts#E.set value'], ['a.ts#E.other']]);
});
