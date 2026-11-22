// Wire types mirroring the C# records in src/CodeShield.Abstractions (camelCase JSON).

export interface SourceSpan {
  startLine: number;
  startColumn: number;
  endLine: number;
  endColumn: number;
}

export interface Reason {
  code: string;
  message: string;
  ruleId: string | null;
  contextId: string | null;
}

export interface CorrectnessRuleRequest {
  analyzerId: string;
  evaluatorId: string;
  options: unknown[];
}

export interface AnalyzeRequest {
  protocol: number;
  command: 'analyze';
  root: string;
  files: string[];
  focusFiles: string[];
  changedConfigFiles: string[];
  isBaseline: boolean;
  correctnessRules: CorrectnessRuleRequest[];
}

export interface TokenizeRequest {
  protocol: number;
  command: 'tokenize';
  root: string;
  files: string[];
}

export type WorkerRequest = AnalyzeRequest | TokenizeRequest;

export interface DecisionEvidence {
  decisionKind: string;
  line: number;
}

export interface NestingEvidence {
  constructKind: string;
  line: number;
  depth: number;
}

export interface MethodFact {
  unitId: string;
  name: string;
  span: SourceSpan;
  complexity: number;
  nesting: number;
  codeLines: number;
  parameterCount: number;
  decisions: DecisionEvidence[];
  nestingPoints: NestingEvidence[];
}

export interface FileFact {
  path: string;
  codeLines: number;
  generated: boolean;
  methods: MethodFact[];
}

export interface CohesionNode {
  id: string;
  name: string;
}

export interface CohesionEdge {
  from: string;
  to: string;
  reason: string;
}

export interface ClassLocation {
  file: string;
  span: SourceSpan;
}

export interface ClassFact {
  unitId: string;
  name: string;
  locations: ClassLocation[];
  applicable: boolean;
  nodes: CohesionNode[];
  edges: CohesionEdge[];
  components: string[][];
}

export interface TypeFact {
  unitId: string;
  file: string;
  isAbstract: boolean;
}

export interface DiagnosticFact {
  analyzerId: string;
  file: string;
  span: SourceSpan;
  message: string;
  unitId: string;
}

export interface ContextFacts {
  contextId: string;
  language: 'typescript';
  configPath: string;
  framework: string | null;
  modulePath: string | null;
  ownedFiles: string[];
  files: FileFact[];
  classes: ClassFact[];
  types: TypeFact[];
  diagnostics: DiagnosticFact[];
  failures: Reason[];
}

export interface ModuleEdge {
  from: string;
  to: string;
  typeOnly: boolean;
}

export interface FileImport {
  from: string;
  to: string;
  typeOnly: boolean;
}

export interface SupportEdge {
  edge: ModuleEdge;
  contextId: string;
}

export interface ContextGraph {
  contextId: string;
  edges: ModuleEdge[];
  fileImports: FileImport[];
  support: SupportEdge[];
}

export interface ModuleGraphFacts {
  modules: string[];
  edges: ModuleEdge[];
  fileImports: FileImport[];
  unresolved: Reason[];
  contexts: ContextGraph[];
}

export interface InputRecord {
  scope: 'repository' | 'external';
  path: string;
  sha256: string;
}

export interface LanguageAnalysis {
  language: 'typescript';
  analyzerVersions: Record<string, string>;
  contexts: ContextFacts[];
  graph: ModuleGraphFacts;
  inputs: InputRecord[];
  unownedFiles: string[];
  failures: Reason[];
}

export interface Token {
  kind: string;
  text: string;
  line: number;
  column: number;
  endLine: number;
  endColumn: number;
  offset: number;
}

export interface FileTokens {
  path: string;
  tokens: Token[];
}

export interface TokenizeResult {
  files: FileTokens[];
  failures: Reason[];
}

export const ReasonCodes = {
  MissingAnalyzer: 'MISSING_ANALYZER',
  MissingDependency: 'MISSING_DEPENDENCY',
  ProjectLoadFailed: 'PROJECT_LOAD_FAILED',
  UnresolvedOwnership: 'UNRESOLVED_OWNERSHIP',
  SuppressedRule: 'SUPPRESSED_RULE',
  UnreadableSource: 'UNREADABLE_SOURCE',
} as const;

export function reason(
  code: string,
  message: string,
  ruleId: string | null = null,
  contextId: string | null = null,
): Reason {
  return { code, message, ruleId, contextId };
}

export function compareReasons(a: Reason, b: Reason): number {
  const key = (r: Reason): string => [r.code, r.ruleId ?? '', r.contextId ?? '', r.message].join('\u0000');
  const [left, right] = [key(a), key(b)];
  if (left < right) {
    return -1;
  }
  return left > right ? 1 : 0;
}
