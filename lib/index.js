// 宿主半侧：拉起并守护贴边助手 DshEdgeDock.exe，把它暴露成本机 HTTP 接口。
//   浏览器半侧（lib/client.js）只跟同源的 /api-dsh-edge-dock/* 说话，
//   所以既不需要跨域，也不需要让页面去起进程。
import { spawn } from 'node:child_process'
import { copyFileSync, existsSync, mkdirSync, statSync } from 'node:fs'
import { connect } from 'node:net'
import { homedir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

export const name = 'dsh-edge-dock'
export const inject = []

const PORT = 59731
const PKG_ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const STATE_DIR = join(process.env.LOCALAPPDATA || join(homedir(), 'AppData', 'Local'), 'DshEdgeDock')
const EXE_PATH = join(STATE_DIR, 'DshEdgeDock.exe')
const EXE_SRC = join(PKG_ROOT, 'bin', 'DshEdgeDock.exe')

function log(...args) {
  try { console.info('[dsh-edge-dock]', ...args) } catch {}
}

/** 把插件自带的 exe 铺到 %LOCALAPPDATA%\DshEdgeDock\（大小不一致才覆盖）。 */
function ensureDeployed() {
  try {
    if (!existsSync(EXE_SRC)) return false
    mkdirSync(STATE_DIR, { recursive: true })
    const src = statSync(EXE_SRC).size
    const dst = existsSync(EXE_PATH) ? statSync(EXE_PATH).size : -1
    if (src !== dst) {
      copyFileSync(EXE_SRC, EXE_PATH)
      log('deployed helper ->', EXE_PATH)
    }
    return true
  } catch (e) {
    log('deploy failed:', e && e.message)
    return false
  }
}

/** 一条命令一个连接：写一行、读一行。 */
function send(command, timeoutMs = 1200) {
  return new Promise((resolveResult) => {
    let done = false
    const finish = (value) => {
      if (done) return
      done = true
      try { socket.destroy() } catch {}
      resolveResult(value)
    }
    let socket
    try {
      socket = connect({ host: '127.0.0.1', port: PORT })
    } catch (e) {
      return resolveResult({ ok: false, running: false, message: 'helper 未运行' })
    }
    const timer = setTimeout(() => finish({ ok: false, running: true, message: 'helper 超时' }), timeoutMs)
    if (timer.unref) timer.unref()
    socket.setEncoding('utf8')
    socket.on('connect', () => { try { socket.write(command + '\n') } catch {} })
    socket.on('data', (chunk) => {
      const line = String(chunk).split('\n')[0].trim()
      clearTimeout(timer)
      if (!line) return finish({ ok: false, running: true, message: '空应答' })
      if (line.startsWith('{')) {
        try { return finish(JSON.parse(line)) } catch {}
      }
      finish({ ok: line === 'ok', running: true, raw: line })
    })
    socket.on('error', () => {
      clearTimeout(timer)
      finish({ ok: false, running: false, message: 'helper 未运行' })
    })
  })
}

function spawnHelper() {
  try {
    if (!existsSync(EXE_PATH)) {
      log('helper exe missing at', EXE_PATH)
      return
    }
    const child = spawn(EXE_PATH, [], { detached: true, stdio: 'ignore', windowsHide: true })
    child.on('error', (e) => log('spawn error:', e && e.message))
    child.unref()
    log('helper spawned pid', child.pid)
  } catch (e) {
    log('spawn failed:', e && e.message)
  }
}

async function ensureRunning() {
  const pong = await send('ping', 500)
  if (pong && pong.running !== false) return true
  spawnHelper()
  await new Promise((r) => setTimeout(r, 900))
  const again = await send('ping', 700)
  return Boolean(again && again.running !== false)
}

function sendJson(res, status, value) {
  res.writeHead(status, { 'content-type': 'application/json; charset=utf-8', 'cache-control': 'no-store' })
  res.end(JSON.stringify(value))
}

async function readJson(req, limit = 64 * 1024) {
  const chunks = []
  let size = 0
  for await (const chunk of req) {
    size += chunk.length
    if (size > limit) break
    chunks.push(chunk)
  }
  const text = Buffer.concat(chunks).toString('utf8')
  if (!text) return {}
  try { return JSON.parse(text) } catch { return {} }
}

const ALLOWED_KEYS = new Set(['paused', 'alwaysOnTop', 'showAboveFullscreen', 'excludeSnapLayout', 'autoStart'])
const ALLOWED_ACTIONS = new Set(['reveal', 'collapse', 'restart', 'quit'])

export function apply(ctx) {
  if (process.platform !== 'win32') {
    log('skip: not windows')
    return
  }
  if (!ensureDeployed()) log('helper binary unavailable; settings page will show it as stopped')

  ensureRunning().then((ok) => log(ok ? 'helper ready' : 'helper not answering')).catch(() => {})

  const timer = setInterval(() => {
    send('ping', 600).then((pong) => {
      if (!pong || pong.running === false) spawnHelper()
    }).catch(() => {})
  }, 8000)
  if (timer.unref) timer.unref()
  if (typeof ctx.effect === 'function') ctx.effect(() => () => clearInterval(timer))
  else if (typeof ctx.on === 'function') ctx.on('dispose', () => clearInterval(timer))

  ctx.inject(['webServer'], (webCtx) => {
    const ws = webCtx.webServer
    if (!ws || typeof ws.register !== 'function') {
      log('webServer service unavailable')
      return
    }

    ws.register({
      kind: 'exact',
      path: '/api-dsh-edge-dock/state',
      handler: async (req, res) => {
        const state = await send('state')
        if (state && state.running !== false && state.ok !== false) return sendJson(res, 200, state)
        return sendJson(res, 200, {
          ok: false,
          running: false,
          message: (state && state.message) || '贴边助手没有运行',
          exePath: EXE_PATH,
        })
      },
    })

    ws.register({
      kind: 'exact',
      path: '/api-dsh-edge-dock/set',
      handler: async (req, res) => {
        if (req.method !== 'POST') return sendJson(res, 405, { ok: false, message: '只接受 POST' })
        const body = await readJson(req)
        const key = String(body.key || '')
        if (!ALLOWED_KEYS.has(key)) return sendJson(res, 400, { ok: false, message: '未知设置项: ' + key })
        await ensureRunning()
        const state = await send('set ' + key + ' ' + (body.value ? '1' : '0'))
        return sendJson(res, 200, state)
      },
    })

    ws.register({
      kind: 'exact',
      path: '/api-dsh-edge-dock/action',
      handler: async (req, res) => {
        if (req.method !== 'POST') return sendJson(res, 405, { ok: false, message: '只接受 POST' })
        const body = await readJson(req)
        const action = String(body.action || '')
        if (!ALLOWED_ACTIONS.has(action)) return sendJson(res, 400, { ok: false, message: '未知操作: ' + action })
        if (action === 'restart') {
          await send('quit', 400).catch(() => {})
          await new Promise((r) => setTimeout(r, 700))
          ensureDeployed()
          spawnHelper()
          await new Promise((r) => setTimeout(r, 1200))
          return sendJson(res, 200, await send('state'))
        }
        if (action === 'quit') {
          const out = await send('quit', 500)
          return sendJson(res, 200, { ...out, running: false })
        }
        await ensureRunning()
        const state = await send(action)
        return sendJson(res, 200, state)
      },
    })

    log('routes ready (state / set / action)')
  })
}
