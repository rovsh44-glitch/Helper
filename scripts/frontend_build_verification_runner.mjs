import childProcess from 'node:child_process';

const originalExec = childProcess.exec.bind(childProcess);

childProcess.exec = function patchedExec(command, ...args) {
  if (typeof command === 'string' && command.trim().toLowerCase() === 'net use') {
    const callback = args.find((arg) => typeof arg === 'function');
    if (callback) {
      process.nextTick(() => callback(new Error('[FrontendBuild] Skipped net use lookup in verification runner.')));
    }

    const stub = {
      pid: 0,
      stdin: null,
      stdout: null,
      stderr: null,
      kill: () => false,
      on() { return stub; },
      once() { return stub; },
      removeListener() { return stub; },
      removeAllListeners() { return stub; },
    };

    return stub;
  }

  return originalExec(command, ...args);
};

const [{ build }, { createHelperViteConfig }] = await Promise.all([
  import('vite'),
  import('../vite.shared.config.mjs'),
]);

const mode = process.env.NODE_ENV || 'production';
await build({
  ...createHelperViteConfig(mode),
  mode,
  configFile: false,
});
