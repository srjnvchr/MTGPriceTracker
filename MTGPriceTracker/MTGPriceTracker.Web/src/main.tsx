import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { IconContext } from '@phosphor-icons/react'
import './index.css'
import { ThemeProvider } from './lib/theme'
import { ToastProvider } from './components/Toast'
import App from './App'

// Cached data is shown instantly on revisit and refreshed in the background.
const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 60_000,
      gcTime: 10 * 60_000,
      refetchOnWindowFocus: false,
      retry: 1,
    },
  },
})

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <ThemeProvider>
        {/* One icon family and weight across the app. */}
        <IconContext.Provider value={{ weight: 'regular', size: 18 }}>
          <ToastProvider>
            <BrowserRouter>
              <App />
            </BrowserRouter>
          </ToastProvider>
        </IconContext.Provider>
      </ThemeProvider>
    </QueryClientProvider>
  </StrictMode>,
)
