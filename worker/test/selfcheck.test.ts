import * as assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { describe, it } from 'node:test';
import { analyze } from '../src/analyze';

const ROOT = path.resolve(__dirname, '..', '..');

function cycleFrom(start: string, graph: Map<string, string[]>): string | undefined {
  const stack: string[] = [];
  const done = new Set<string>();
  const visit = (node: string): string | undefined => {
    if (stack.includes(node)) {
      return [...stack.slice(stack.indexOf(node)), node].join(' -> ');
    }
    if (done.has(node)) {
      return undefined;
    }
    stack.push(node);
    const found = (graph.get(node) ?? []).map(visit).find((c) => c !== undefined);
    stack.pop();
    done.add(node);
    return found;
  };
  return visit(start);
}

describe('worker self-check against the CodeShield quality limits', () => {
  const files = fs.readdirSync(path.join(ROOT, 'src')).filter((f) => f.endsWith('.ts')).map((f) => `src/${f}`);
  const resultPromise = analyze({
    protocol: 1,
    command: 'analyze',
    root: ROOT,
    files: [...files, 'tsconfig.json', 'package.json'],
    focusFiles: files,
    changedConfigFiles: [],
    isBaseline: false,
    correctnessRules: [],
  });

  it('every function meets complexity, nesting, length and parameter limits', async () => {
    const context = (await resultPromise).contexts.find((c) => c.contextId === 'tsconfig.json')!;
    const methods = context.files.flatMap((f) => f.methods.map((m) => ({ f: f.path, m })));
    assert.ok(methods.length > 50);
    const bad = methods.filter(({ m }) => m.complexity > 6 || m.nesting > 2 || m.codeLines > 40 || m.parameterCount > 5);
    assert.deepEqual(bad.map(({ f, m }) => `${f}:${m.unitId}`), []);
  });

  it('files are at most 400 code lines', async () => {
    const context = (await resultPromise).contexts.find((c) => c.contextId === 'tsconfig.json')!;
    assert.deepEqual(context.files.filter((f) => f.codeLines > 400).map((f) => f.path), []);
  });

  it('has no value-import cycles', async () => {
    const { graph } = await resultPromise;
    const adjacency = new Map<string, string[]>();
    for (const edge of graph.fileImports.filter((e) => !e.typeOnly)) {
      adjacency.set(edge.from, [...(adjacency.get(edge.from) ?? []), edge.to]);
    }
    assert.deepEqual([...adjacency.keys()].map((k) => cycleFrom(k, adjacency)).filter(Boolean), []);
  });
});
