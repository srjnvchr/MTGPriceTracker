import { lazy, Suspense } from 'react'
import { Route, Routes } from 'react-router-dom'
import Layout from './components/Layout'
import { Spinner } from './components/ui'
import Home from './pages/Home'
import Cards from './pages/Cards'
import Favorites from './pages/Favorites'

// Heavier / rarely-used pages are code-split so the first load stays small.
const CardDetail = lazy(() => import('./pages/CardDetail'))
const Sync = lazy(() => import('./pages/Sync'))
const GoodGamesInspector = lazy(() => import('./pages/GoodGamesInspector'))

export default function App() {
  return (
    <Layout>
      <Suspense
        fallback={
          <div className="flex justify-center pt-24">
            <Spinner />
          </div>
        }
      >
        <Routes>
          <Route path="/" element={<Home />} />
          <Route path="/cards" element={<Cards />} />
          <Route path="/cards/:uuid" element={<CardDetail />} />
          <Route path="/favorites" element={<Favorites />} />
          <Route path="/sync" element={<Sync />} />
          <Route path="/debug/goodgames" element={<GoodGamesInspector />} />
          <Route path="*" element={<Home />} />
        </Routes>
      </Suspense>
    </Layout>
  )
}
