import { createRouter, createWebHashHistory } from 'vue-router'
import { isLoggedIn } from './api/fundApi'
import HomeView from './views/HomeView.vue'

const routes = [
  { path: '/login', name: 'login', component: () => import('./views/LoginView.vue') },
  { path: '/', name: 'home', component: HomeView, meta: { requiresAuth: true } },
  { path: '/sector', name: 'sector', component: () => import('./views/SectorView.vue'), meta: { requiresAuth: true } },
  { path: '/news', name: 'news', component: () => import('./views/NewsView.vue'), meta: { requiresAuth: true } },
  { path: '/analysis', name: 'analysis', component: () => import('./views/AnalysisView.vue'), meta: { requiresAuth: true } }
]

const router = createRouter({
  history: createWebHashHistory(),
  routes
})

router.beforeEach((to, from, next) => {
  if (to.meta.requiresAuth && !isLoggedIn()) {
    next({ name: 'login' })
  } else if (to.name === 'login' && isLoggedIn()) {
    next({ name: 'home' })
  } else {
    next()
  }
})

export default router
