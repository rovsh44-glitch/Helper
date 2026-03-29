import { mkdir, writeFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { spawn } from 'node:child_process';
import process from 'node:process';
import WebSocket from 'ws';

let nextId = 0;

const EDGE_PATHS = [
  'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
  'C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe',
  'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',
];

const url = process.argv[2] ?? 'http://127.0.0.1:4173';
const outputPath = resolve(process.argv[3] ?? 'C:\\HELPER_DATA\\helper_screenshot.png');
const remoteDebuggingPort = Number(process.argv[4] ?? '9223');
const tabLabel = process.argv[5] ?? 'Evolution';
const settleDelayMs = Number(process.argv[6] ?? '3000');

const edgePath = EDGE_PATHS.find(Boolean);
if (!edgePath) {
  throw new Error('Browser executable not found.');
}

const browserProfileDir = 'C:\\HELPER_DATA\\edge-capture-profile';
await mkdir(browserProfileDir, { recursive: true });
await mkdir(dirname(outputPath), { recursive: true });

const browser = spawn(edgePath, [
  `--remote-debugging-port=${remoteDebuggingPort}`,
  '--headless=new',
  '--disable-gpu',
  '--disable-extensions',
  '--hide-scrollbars',
  `--user-data-dir=${browserProfileDir}`,
  'about:blank',
], {
  stdio: 'ignore',
  detached: false,
});

let socket;

try {
  const target = await waitForPageTarget(remoteDebuggingPort, 15000);
  socket = await connect(target.webSocketDebuggerUrl);
  await send(socket, 'Page.enable');
  await send(socket, 'Runtime.enable');
  await send(socket, 'Emulation.setDeviceMetricsOverride', {
    width: 1440,
    height: 1024,
    deviceScaleFactor: 1,
    mobile: false,
  });
  await send(socket, 'Page.navigate', { url });
  await waitForDocumentReady(socket, 15000);
  await waitForButton(socket, tabLabel, 15000);
  await clickButton(socket, tabLabel);
  await delay(Number.isFinite(settleDelayMs) ? Math.max(1000, settleDelayMs) : 3000);
  await delay(2500);
  const screenshot = await send(socket, 'Page.captureScreenshot', {
    format: 'png',
    fromSurface: true,
  });
  await writeFile(outputPath, screenshot.data, 'base64');
  console.log(`Saved screenshot: ${outputPath}`);
} finally {
  if (socket) {
    socket.close();
  }
  if (browser.pid) {
    spawn('taskkill', ['/PID', String(browser.pid), '/T', '/F'], { stdio: 'ignore' });
  }
}

async function waitForPageTarget(port, timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    try {
      const response = await fetch(`http://127.0.0.1:${port}/json/list`);
      if (response.ok) {
        const targets = await response.json();
        const pageTarget = targets.find(target => target.type === 'page' && target.webSocketDebuggerUrl);
        if (pageTarget) {
          return pageTarget;
        }
      }
    } catch {
      // Browser is still starting.
    }
    await delay(250);
  }

  throw new Error('Timed out waiting for browser target.');
}

function connect(webSocketUrl) {
  return new Promise((resolvePromise, rejectPromise) => {
    const ws = new WebSocket(webSocketUrl);
    ws.once('open', () => resolvePromise(ws));
    ws.once('error', rejectPromise);
  });
}

function send(ws, method, params = {}) {
  const id = nextId++;
  return new Promise((resolvePromise, rejectPromise) => {
    const onMessage = raw => {
      const message = JSON.parse(raw.toString());
      if (message.id !== id) {
        return;
      }

      ws.off('message', onMessage);
      if (message.error) {
        rejectPromise(new Error(`${method} failed: ${message.error.message}`));
        return;
      }

      resolvePromise(message.result ?? {});
    };

    ws.on('message', onMessage);
    ws.send(JSON.stringify({ id, method, params }));
  });
}

async function evaluate(ws, expression) {
  const result = await send(ws, 'Runtime.evaluate', {
    expression,
    awaitPromise: true,
    returnByValue: true,
  });

  return result.result?.value;
}

async function waitForDocumentReady(ws, timeoutMs) {
  await waitForCondition(ws, `
    (() => document.readyState === 'complete')()
  `, timeoutMs, 'Document did not reach readyState=complete.');
}

async function waitForButton(ws, label, timeoutMs) {
  await waitForCondition(ws, `
    (() => [...document.querySelectorAll('button')].some(button => (button.innerText || '').includes(${JSON.stringify(label)})))()
  `, timeoutMs, `Button "${label}" not found.`);
}

async function clickButton(ws, label) {
  const clicked = await evaluate(ws, `
    (() => {
      const button = [...document.querySelectorAll('button')].find(candidate => (candidate.innerText || '').includes(${JSON.stringify(label)}));
      if (!button) {
        return false;
      }
      button.click();
      return true;
    })()
  `);

  if (!clicked) {
    throw new Error(`Unable to click button "${label}".`);
  }
}

async function waitForText(ws, text, timeoutMs) {
  await waitForCondition(ws, `
    (() => document.body?.innerText?.includes(${JSON.stringify(text)}) ?? false)()
  `, timeoutMs, `Text "${text}" did not appear.`);
}

async function waitForCondition(ws, expression, timeoutMs, errorMessage) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    if (await evaluate(ws, expression)) {
      return;
    }
    await delay(250);
  }

  throw new Error(errorMessage);
}

function delay(timeoutMs) {
  return new Promise(resolvePromise => setTimeout(resolvePromise, timeoutMs));
}
