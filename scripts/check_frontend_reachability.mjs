import { readFileSync, readdirSync, statSync } from 'node:fs';
import { dirname, extname, join, resolve } from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const repoRoot = resolve(__dirname, '..');
const budgets = JSON.parse(readFileSync(join(__dirname, 'performance_budgets.json'), 'utf8'));
const maxUnreachableModules = Number(budgets.frontend?.maxUnreachableModules ?? 0);
const candidateExtensions = ['.ts', '.tsx', '.js', '.jsx', '.mjs'];
const sourceRoots = ['components', 'contexts', 'hooks', 'services', 'utils'];
const rootSourceFiles = ['App.tsx', 'index.tsx', 'types.ts'];

function safeIsFile(path) {
  try {
    return statSync(path).isFile();
  } catch {
    return false;
  }
}

function safeIsDirectory(path) {
  try {
    return statSync(path).isDirectory();
  } catch {
    return false;
  }
}

function normalizePath(path) {
  return resolve(path).replace(/\\/g, '/');
}

function walk(currentPath, files) {
  for (const entry of readdirSync(currentPath, { withFileTypes: true })) {
    if (entry.name === 'node_modules' || entry.name === 'dist') {
      continue;
    }

    const fullPath = join(currentPath, entry.name);
    if (entry.isDirectory()) {
      walk(fullPath, files);
      continue;
    }

    if (entry.isFile()) {
      files.push(fullPath);
    }
  }
}

function collectSourceFiles() {
  const files = [];

  for (const rootFile of rootSourceFiles) {
    const fullPath = join(repoRoot, rootFile);
    if (safeIsFile(fullPath)) {
      files.push(fullPath);
    }
  }

  for (const sourceRoot of sourceRoots) {
    const fullPath = join(repoRoot, sourceRoot);
    if (safeIsDirectory(fullPath)) {
      walk(fullPath, files);
    }
  }

  return files
    .filter(path => candidateExtensions.includes(extname(path)))
    .map(normalizePath)
    .sort((left, right) => left.localeCompare(right));
}

function resolveImport(fromFile, specifier) {
  if (!specifier.startsWith('.')) {
    return null;
  }

  const basePath = resolve(dirname(fromFile), specifier);
  const candidates = [];

  if (extname(basePath)) {
    candidates.push(basePath);
  } else {
    for (const extension of candidateExtensions) {
      candidates.push(`${basePath}${extension}`);
      candidates.push(join(basePath, `index${extension}`));
    }
  }

  for (const candidate of candidates) {
    if (safeIsFile(candidate)) {
      return normalizePath(candidate);
    }
  }

  return null;
}

function extractDependencies(filePath) {
  const content = readFileSync(filePath, 'utf8');
  const patterns = [
    /\bimport\s+[^'"]*?from\s+['"]([^'"]+)['"]/g,
    /\bexport\s+[^'"]*?from\s+['"]([^'"]+)['"]/g,
    /\bimport\s*\(\s*['"]([^'"]+)['"]\s*\)/g,
  ];
  const dependencies = new Set();

  for (const pattern of patterns) {
    for (const match of content.matchAll(pattern)) {
      const resolvedImport = resolveImport(filePath, match[1]);
      if (resolvedImport) {
        dependencies.add(resolvedImport);
      }
    }
  }

  return [...dependencies];
}

function toRelativePath(path) {
  return normalizePath(path).replace(`${normalizePath(repoRoot)}/`, '');
}

function findCycles(graph) {
  const visited = new Set();
  const active = new Set();
  const stack = [];
  const cycles = new Set();

  function dfs(node) {
    if (active.has(node)) {
      const cycleStart = stack.indexOf(node);
      if (cycleStart >= 0) {
        const cycle = stack.slice(cycleStart).concat(node).map(toRelativePath);
        cycles.add(cycle.join(' -> '));
      }

      return;
    }

    if (visited.has(node)) {
      return;
    }

    visited.add(node);
    active.add(node);
    stack.push(node);

    for (const dependency of graph.get(node) ?? []) {
      dfs(dependency);
    }

    stack.pop();
    active.delete(node);
  }

  for (const node of graph.keys()) {
    dfs(node);
  }

  return [...cycles].sort((left, right) => left.localeCompare(right));
}

const files = collectSourceFiles();
const graph = new Map(files.map(file => [file, extractDependencies(file)]));
const entryPoint = normalizePath(join(repoRoot, 'index.tsx'));
const reachable = new Set();
const queue = [entryPoint];

while (queue.length > 0) {
  const current = queue.shift();
  if (!current || reachable.has(current)) {
    continue;
  }

  reachable.add(current);
  for (const dependency of graph.get(current) ?? []) {
    if (!reachable.has(dependency)) {
      queue.push(dependency);
    }
  }
}

const unreachable = [...graph.keys()]
  .filter(file => !reachable.has(file))
  .map(toRelativePath)
  .sort((left, right) => left.localeCompare(right));
const cycles = findCycles(graph);

if (cycles.length > 0) {
  console.error(`[FrontendGate] Import cycles detected (${cycles.length}):`);
  for (const cycle of cycles) {
    console.error(` - ${cycle}`);
  }
  process.exit(1);
}

if (unreachable.length > maxUnreachableModules) {
  console.error(`[FrontendGate] Unreachable modules detected (${unreachable.length}) > budget ${maxUnreachableModules}:`);
  for (const file of unreachable) {
    console.error(` - ${file}`);
  }
  process.exit(1);
}

console.log(`[FrontendGate] Reachability checks passed. modules=${graph.size}, unreachable=${unreachable.length}, cycles=${cycles.length}.`);
