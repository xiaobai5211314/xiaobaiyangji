import { createRouter, createWebHashHistory } from 'vue-router'
import HomeView from './views/HomeView.vue'

const routes = [
  { path: '/', name: 'home', component: HomeView },
  { path: '/sector', name: 'sector', component: () => import('./views/SectorView.vue') },
  { path: '/news', name: 'news', component: () => import('./views/NewsView.vue') },
  { path: '/analysis', name: 'analysis', component: () => import('./views/AnalysisView.vue') }
]

export default createRouter({
  history: createWebHashHistory(),
  routes
})
