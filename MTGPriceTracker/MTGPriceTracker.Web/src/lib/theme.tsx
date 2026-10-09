import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'

type Mode = 'system' | 'light' | 'dark'
type Resolved = 'light' | 'dark'

interface ThemeCtx {
  mode: Mode
  resolved: Resolved
  toggle: () => void
}

const STORAGE_KEY = 'mtgpt-theme'
const Ctx = createContext<ThemeCtx>({ mode: 'system', resolved: 'dark', toggle: () => {} })

// eslint-disable-next-line react-refresh/only-export-components
export const useTheme = () => useContext(Ctx)

const systemPrefersDark = () =>
  typeof window !== 'undefined' && window.matchMedia('(prefers-color-scheme: dark)').matches

function readStored(): Mode {
  try {
    const v = localStorage.getItem(STORAGE_KEY)
    return v === 'light' || v === 'dark' ? v : 'system'
  } catch {
    return 'system'
  }
}

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [mode, setMode] = useState<Mode>(readStored)
  const [systemDark, setSystemDark] = useState(systemPrefersDark)

  useEffect(() => {
    const mq = window.matchMedia('(prefers-color-scheme: dark)')
    const onChange = (e: MediaQueryListEvent) => setSystemDark(e.matches)
    mq.addEventListener('change', onChange)
    return () => mq.removeEventListener('change', onChange)
  }, [])

  useEffect(() => {
    const root = document.documentElement
    if (mode === 'system') root.removeAttribute('data-theme')
    else root.setAttribute('data-theme', mode)
  }, [mode])

  const resolved: Resolved = mode === 'system' ? (systemDark ? 'dark' : 'light') : mode

  const toggle = useCallback(() => {
    const next: Mode = resolved === 'dark' ? 'light' : 'dark'
    setMode(next)
    try {
      localStorage.setItem(STORAGE_KEY, next)
    } catch {
      /* storage unavailable: theme still applies for this session */
    }
  }, [resolved])

  const value = useMemo(() => ({ mode, resolved, toggle }), [mode, resolved, toggle])
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>
}
