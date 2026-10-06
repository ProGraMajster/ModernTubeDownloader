// Bounded publication audit. Reports locations/rules only, never matched values.
'use strict';
const fs = require('node:fs');
const path = require('node:path');
const { execFileSync } = require('node:child_process');
const root = path.resolve(__dirname, '..');
const git = (...args) => execFileSync('git', args, { cwd: root, maxBuffer: 16 * 1024 * 1024 });
const rules = [
  ['private-key', /-----BEGIN (?:RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----/g],
  ['github-token', /\b(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{40,})\b/g],
  ['provider-token', /\b(?:AKIA[A-Z0-9]{16}|AIza[A-Za-z0-9_-]{35}|xox[baprs]-[A-Za-z0-9-]{20,})\b/g],
  ['credential-url', /https?:\/\/[^\s/"'<>]+:[^\s/"'<>]+@/g],
  ['signed-url', /https?:\/\/[^\s"'<>]*[?&](?:X-Amz-Signature|sig|signature|access_token)=[^\s"'<>]{12,}/gi],
  ['personal-path', /(?:[A-Z]:[\\/]+Users[\\/]+(?!Public\b|Default\b)[A-Za-z0-9_.-]+|\/home\/[A-Za-z0-9_.-]+)[\\/]/gi],
  ['private-lan-url', /https?:\/\/(?:192\.168\.\d+\.\d+|10\.\d+\.\d+\.\d+|172\.(?:1[6-9]|2\d|3[01])\.\d+\.\d+)(?=[:/\s"'])/g],
  ['secret-assignment', /\b(?:password|api[_-]?key|client[_-]?secret|access[_-]?token)\s*[=:]\s*["'][A-Za-z0-9+/_=-]{24,}["']/gi],
  ['bearer-literal', /(?:Authorization|Bearer)[\s:"'=]+Bearer\s+[A-Za-z0-9_.-]{24,}/gi]
];
const allowedBinary = new Set([
  'ModernTubeDownloader/Assets/AppIcon.png', 'ModernTubeDownloader/Assets/AppIcon.ico',
  ...[16, 32, 48, 128].map(n => `browser-extension/assets/icon${n}.png`)
]);
const findings = [];
// Reviewed synthetic security-test inputs only; never exempt an entire test file.
const syntheticFixtures = new Map([
  ['ModernTubeDownloader.Tests/DownloadEnhancementTests.cs:personal-path', ['C:\\Users\\Someone\\']],
  ['ModernTubeDownloader.Tests/ExternalLinkTests.cs:credential-url', ['https://user:pass@']],
  ['ModernTubeDownloader.Tests/SupportedSourcesTests.cs:credential-url', ['https://user:password@']],
  ['ModernTubeDownloader.Tests/WebRemoteIntegrationTests.cs:private-lan-url', ['http://192.168.1.50']]
]);
let reviewedFixtureCount = 0;
let textCount = 0, binaryCount = 0;
function inspect(scope, name, buffer) {
  if (/(?:^|\/)(?:bin|obj|artifacts|\.codex|\.vs|test-profiles|qa-profiles|CrashReports|screenshots|\.mfn-[^/]*)(?:\/|$)|(?:^|\/)\.env(?:\.|$)|\.(?:pdb|log|mp4|mkv|webm|zip|exe|dll|trx)$|(?:^|\/)(?:cookies|settings|history|queue)\.json$/i.test(name)) {
    findings.push({ scope, path: name, rule: 'forbidden-path' });
  }
  if (buffer.includes(0)) {
    binaryCount++;
    if (!allowedBinary.has(name)) findings.push({ scope, path: name, rule: 'unexpected-binary' });
    return;
  }
  if (buffer.length > 2 * 1024 * 1024) { findings.push({ scope, path: name, rule: 'oversize-text' }); return; }
  textCount++;
  const content = buffer.toString('utf8');
  for (const [rule, regex] of rules) {
    regex.lastIndex = 0;
    for (const match of content.matchAll(regex)) {
      const normalized = match[0].replace(/\\\\/g, '\\');
      const auditedScannerFixture = name === 'scripts/Test-PublicRepository.cjs' &&
        [...syntheticFixtures.entries()].some(([key, values]) => key.endsWith(`:${rule}`) && values.includes(normalized));
      if (syntheticFixtures.get(`${name}:${rule}`)?.includes(normalized) || auditedScannerFixture) { reviewedFixtureCount++; continue; }
      findings.push({ scope, path: name, line: content.slice(0, match.index).split('\n').length, rule });
    }
  }
}
const files = [...new Set(git('ls-files', '--cached', '--others', '--exclude-standard', '-z').toString().split('\0').filter(Boolean))];
for (const file of files) if (fs.existsSync(path.join(root, file))) inspect('working-tree', file, fs.readFileSync(path.join(root, file)));
const seen = new Set();
const commits = git('rev-list', '--all').toString().trim().split('\n').filter(Boolean);
for (const commit of commits) {
  for (const entry of git('ls-tree', '-rz', '--full-tree', commit).toString().split('\0').filter(Boolean)) {
    const [metadata, name] = entry.split('\t');
    const [, type, object] = metadata.split(' ');
    if (type !== 'blob' || seen.has(`${object}:${name}`)) continue;
    seen.add(`${object}:${name}`);
    inspect(`history:${commit.slice(0, 12)}`, name, git('cat-file', 'blob', object));
  }
}
console.log(JSON.stringify({ candidates: files.length, commits: commits.length, historicalBlobs: seen.size, textCount, binaryCount, reviewedFixtureCount, findings }, null, 2));
if (findings.length) process.exitCode = 1;
