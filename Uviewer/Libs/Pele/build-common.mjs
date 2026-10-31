// Build-time only: node build-common.mjs <extracted pele@1.12.0 package> <esbuild@0.28.2 package>
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { readFile } from 'node:fs/promises';

const [peleDirectory, esbuildDirectory] = process.argv.slice(2);
if (!peleDirectory || !esbuildDirectory) throw new Error('Provide the Pele and esbuild package directories.');
const pele = JSON.parse(await readFile(path.join(peleDirectory, 'package.json'), 'utf8'));
const esbuildPackage = JSON.parse(await readFile(path.join(esbuildDirectory, 'package.json'), 'utf8'));
if (pele.version !== '1.12.0' || esbuildPackage.version !== '0.28.2') throw new Error('Unexpected package version.');
const { build } = await import(pathToFileURL(path.resolve(esbuildDirectory, 'lib/main.js')).href);
await build({
  stdin: {
    contents: `
      export { render, supports, forgetTextWidths } from './dist/esm/core.js';
      import { register } from './dist/esm/core.js';
      import flowchart from './dist/esm/diagrams/flowchart.js';
      import sequence from './dist/esm/diagrams/sequence.js';
      register(flowchart, sequence);
    `,
    resolveDir: path.resolve(peleDirectory),
    sourcefile: 'pele-common.js',
  },
  outfile: path.join(path.dirname(fileURLToPath(import.meta.url)), 'pele.common.global.js'),
  bundle: true,
  format: 'iife',
  globalName: 'pele',
  platform: 'neutral',
  target: 'es2022',
  minify: true,
  logOverride: { 'ignored-bare-import': 'silent' },
  legalComments: 'none',
  banner: { js: '/*! Pele 1.12.0 (flowchart/sequence) | MIT License | https://github.com/obsidianmd/pele */' },
});
