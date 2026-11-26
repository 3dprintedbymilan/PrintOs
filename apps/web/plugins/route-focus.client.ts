export default defineNuxtPlugin(() => {
  useRouter().afterEach((to, from) => {
    if (from.matched.length === 0 || to.path === from.path) return
    nextTick(() => {
      document.querySelector<HTMLElement>('h1')?.focus({ preventScroll: true })
    })
  })
})
