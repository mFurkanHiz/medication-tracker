import { existsSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import type { NextConfig } from 'next';

/**
 * ADR 0017: the one version line. The repository's VERSION file is read at build time and
 * baked into the static export, so the footer says the number the tag was cut from. The
 * commit is handed in by the image build (GIT_SHA) and stays empty for a local build.
 */
function repositoryVersion(): string {
  let directory = process.cwd();

  for (;;) {
    const candidate = join(directory, 'VERSION');
    if (existsSync(candidate)) {
      return readFileSync(candidate, 'utf8').trim();
    }

    const parent = dirname(directory);
    if (parent === directory) {
      throw new Error('VERSION was not found above ' + process.cwd());
    }
    directory = parent;
  }
}

const nextConfig: NextConfig = {
  output: 'export',
  env: {
    NEXT_PUBLIC_APP_VERSION: repositoryVersion(),
    NEXT_PUBLIC_GIT_SHA: process.env.GIT_SHA ?? '',
  },
};

export default nextConfig;
