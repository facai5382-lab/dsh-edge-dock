// 浏览器半侧：设置 → 贴边自动收起。
// 只跟同源的 /api-dsh-edge-dock/* 通信，由宿主半侧转给 DshEdgeDock.exe。
window.__ModuleLoader__.load({
  id: 'dsh-edge-dock',
  factory: (require) => {
    const react = require('react')
    const h = react.createElement
    const { useState, useEffect, useCallback } = react

    const API = '/api-dsh-edge-dock'

    const C = {
      text: 'var(--dsw-alias-label-primary, #1f2328)',
      sub: 'var(--dsw-alias-label-secondary, #888)',
      faint: 'var(--dsw-alias-label-tertiary, #999)',
      border: 'var(--dsw-alias-border-l3, rgba(127,127,127,.25))',
      accent: 'var(--dsw-alias-link, #2f6bff)',
      danger: '#c94040',
      ok: '#2a9d5c',
    }

    async function api(path, body) {
      const r = await fetch(API + '/' + path, body
        ? { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) }
        : undefined)
      return r.json()
    }

    function Toggle({ on, disabled, onChange }) {
      return h('button', {
        type: 'button',
        role: 'switch',
        'aria-checked': on ? 'true' : 'false',
        disabled: Boolean(disabled),
        onClick: disabled ? undefined : () => onChange(!on),
        style: {
          appearance: 'none', border: 'none', padding: 0, flex: 'none',
          width: 38, height: 22, borderRadius: 11, cursor: disabled ? 'default' : 'pointer',
          background: on ? C.accent : 'rgba(127,127,127,.35)',
          opacity: disabled ? 0.55 : 1,
          transition: 'background .15s',
          position: 'relative',
        },
      }, h('span', {
        style: {
          position: 'absolute', top: 3, left: on ? 19 : 3,
          width: 16, height: 16, borderRadius: 8, background: '#fff',
          transition: 'left .15s', boxShadow: '0 1px 2px rgba(0,0,0,.25)',
        },
      }))
    }

    function Row({ title, desc, control, disabled }) {
      return h('div', {
        style: {
          display: 'flex', alignItems: 'flex-start', gap: 12,
          padding: '10px 0', borderTop: '1px solid ' + C.border,
          opacity: disabled ? 0.5 : 1,
        },
      },
        h('div', { style: { flex: 1, minWidth: 0 } },
          h('div', { style: { fontSize: 13, fontWeight: 500, color: C.text } }, title),
          desc ? h('div', { style: { fontSize: 12, color: C.sub, lineHeight: 1.6, marginTop: 2 } }, desc) : null),
        h('div', { style: { paddingTop: 1 } }, control))
    }

    function Btn({ onClick, children, primary, disabled, danger }) {
      return h('button', {
        type: 'button', onClick, disabled,
        style: {
          appearance: 'none', fontFamily: 'inherit', fontSize: 12, lineHeight: '20px',
          padding: '3px 12px', borderRadius: 7, cursor: disabled ? 'default' : 'pointer',
          opacity: disabled ? 0.55 : 1, whiteSpace: 'nowrap',
          border: '1px solid ' + (danger ? C.danger : primary ? C.accent : C.border),
          background: primary ? C.accent : 'transparent',
          color: primary ? '#fff' : danger ? C.danger : C.text,
        },
      }, children)
    }

    const SIDE_TEXT = { Right: '贴右侧边缘', Left: '贴左侧边缘', None: '还没贴边' }

    function SettingsSection() {
      const [state, setState] = useState(null)
      const [busy, setBusy] = useState('')
      const [note, setNote] = useState('')

      const refresh = useCallback(async () => {
        try { setState(await api('state')) } catch (e) { setState({ ok: false, running: false, message: String(e && e.message || e) }) }
      }, [])

      useEffect(() => {
        let alive = true
        const tick = () => { if (alive) refresh() }
        tick()
        const timer = setInterval(tick, 2000)
        return () => { alive = false; clearInterval(timer) }
      }, [refresh])

      const setKey = async (key, value) => {
        setBusy(key)
        setNote('')
        try {
          const next = await api('set', { key, value })
          if (next && next.ok !== false) setState(next)
          else setNote((next && next.message) || '设置没生效')
        } catch (e) { setNote(String(e && e.message || e)) }
        setBusy('')
      }

      const act = async (action) => {
        setBusy(action)
        setNote('')
        try {
          const next = await api('action', { action })
          if (next && next.ok !== false) {
            setState(next)
            if (action === 'collapse' && next.collapsed !== true) setNote('窗口没贴边，先把它拖到屏幕左右边缘')
          } else setNote((next && next.message) || '没成功')
        } catch (e) { setNote(String(e && e.message || e)) }
        setBusy('')
      }

      const running = Boolean(state && state.running)
      const enabled = Boolean(state && !state.paused)

      const statusDot = h('span', {
        style: {
          display: 'inline-block', width: 7, height: 7, borderRadius: 4, marginRight: 6,
          background: running ? C.ok : C.danger,
        },
      })

      const statusText = !state ? '读取中…'
        : !running ? '助手未运行'
          : '运行中 · ' + (SIDE_TEXT[state.docked] || state.docked || '未知') +
            (state.collapsed ? ' · 已收起' : '') +
            (state.alwaysOnTop ? ' · 置顶' : '')

      return h('div', { style: { color: C.text, maxWidth: 640 } },
        h('div', { style: { fontSize: 15, fontWeight: 600, marginBottom: 2 } }, '贴边自动收起'),
        h('div', { style: { fontSize: 12.5, color: C.sub, lineHeight: 1.7 } },
          '把 Harness 拖到屏幕左/右边缘贴着，点窗口外面它自动收进边缘；鼠标碰到那条边它自动滑出来。'),

        h('div', {
          style: {
            display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap',
            marginTop: 14, padding: '10px 12px', borderRadius: 9,
            border: '1px solid ' + C.border, background: 'rgba(127,127,127,.06)',
          },
        },
          h('div', { style: { fontSize: 12.5, flex: 1, minWidth: 180 } }, statusDot, statusText),
          h(Btn, { onClick: () => act('reveal'), disabled: !running || busy === 'reveal' }, '立即唤出'),
          h(Btn, { onClick: () => act('collapse'), disabled: !running || busy === 'collapse' }, '立即收起'),
          h(Btn, { onClick: () => act('restart'), disabled: busy === 'restart' }, busy === 'restart' ? '重启中…' : '重启助手')),

        !running && state
          ? h('div', { style: { fontSize: 12, color: C.danger, marginTop: 8 } },
              '助手没有响应。点「重启助手」试一次；还不行就检查 %LOCALAPPDATA%\\DshEdgeDock\\edgedock.log。')
          : null,

        h('div', { style: { marginTop: 16 } },
          h(Row, {
            title: '启用贴边自动收起',
            desc: '关掉后不再自动收起，窗口位置不受影响。',
            control: h(Toggle, {
              on: enabled,
              disabled: !running || busy === 'paused',
              onChange: (v) => setKey('paused', !v),
            }),
          }),

          h(Row, {
            title: '窗口置顶',
            desc: 'Harness 始终待在其他窗口上面。收起后不占最前，唤出时自动回到最前。',
            control: h(Toggle, {
              on: Boolean(state && state.alwaysOnTop),
              disabled: !running || busy === 'alwaysOnTop',
              onChange: (v) => setKey('alwaysOnTop', v),
            }),
          }),

          h(Row, {
            title: '其他窗口全屏时也能唤出',
            desc: '看全屏视频/玩游戏时，鼠标碰到边缘仍然把 Harness 提到最前；点外面收起时自动让回。',
            control: h(Toggle, {
              on: Boolean(state && state.showAboveFullscreen),
              disabled: !running || busy === 'showAboveFullscreen',
              onChange: (v) => setKey('showAboveFullscreen', v),
            }),
          }),

          h(Row, {
            title: 'Windows 窗口布局的窗口保持原样',
            desc: '用「贴靠布局」摆成半屏/三分屏的窗口不自动收起，只有自己拖到边缘的才收。',
            control: h(Toggle, {
              on: Boolean(state && state.excludeSnapLayout),
              disabled: !running || busy === 'excludeSnapLayout',
              onChange: (v) => setKey('excludeSnapLayout', v),
            }),
          }),

          h(Row, {
            title: '开机自动启动',
            desc: '登录 Windows 后自动运行贴边助手，不必等 Harness 启动。',
            control: h(Toggle, {
              on: Boolean(state && state.autoStart),
              disabled: !running || busy === 'autoStart',
              onChange: (v) => setKey('autoStart', v),
            }),
          })),

        note ? h('div', { style: { fontSize: 12, color: C.danger, marginTop: 10 } }, note) : null,

        h('div', { style: { fontSize: 12, color: C.faint, lineHeight: 1.7, marginTop: 16 } },
          state && state.version ? '助手版本 ' + state.version + ' · ' : '',
          '助手是一个独立的小程序（不是 Harness 进程的一部分），所以 Harness 更新不会把它覆盖；',
          '它随插件自动启动，也可以在托盘图标上右键暂停或退出。'))
    }

    function Icon({ size = 16 }) {
      return h('svg', { width: size, height: size, viewBox: '0 0 24 24', fill: 'none' },
        h('rect', { x: 3, y: 5, width: 12, height: 14, rx: 2, stroke: 'currentColor', strokeWidth: 2 }),
        h('path', { d: 'M18 9l3 3-3 3', stroke: 'currentColor', strokeWidth: 2, strokeLinecap: 'round', strokeLinejoin: 'round' }))
    }

    const LABEL = '贴边自动收起'

    // 客户端插件必须声明用到 slots 服务，否则 apply 时 ctx.slots 还没就位，
    // 注册会静默失败（设置页里既没有报错也没有入口）。
    const inject = ['slots']

    function apply(ctx) {
      const register = (name, desc, component) => {
        try {
          ctx.slots.inject(name, () => ctx.slots.register({ name, ...desc }, component))
        } catch (error) {
          console.error('[dsh-edge-dock] 注册槽位失败：' + name, error)
        }
      }

      register('settings.section', { id: 'dsh-edge-dock', order: 62, label: () => LABEL }, SettingsSection)
      register('settings.section.icon', { id: 'dsh-edge-dock' }, Icon)
    }

    return { name: 'dsh-edge-dock', apply, inject }
  },
})
