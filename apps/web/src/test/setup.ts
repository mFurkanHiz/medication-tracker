import { cleanup } from '@testing-library/react';
import { afterEach } from 'vitest';

/**
 * Unmounts whatever a test rendered once it is over.
 *
 * React Testing Library only registers this itself when the test runner exposes a global
 * `afterEach`, and Vitest does not unless asked to. Without it every `render` is appended
 * to the same document and left there, so the third test to render a panel finds three of
 * them — and `getByText` correctly refuses to choose. That failure reads like a bug in the
 * component. It is not.
 */
afterEach(() => {
  cleanup();
});
