export default defineNuxtRouteMiddleware((to) => {
  if (to.meta.public === true) return

  const auth = useAuthStore()
  const isSignInRoute = to.path === '/sign-in'

  if (!auth.isSignedIn && !isSignInRoute) return navigateTo('/sign-in')
  if (auth.isSignedIn && isSignInRoute) return navigateTo('/')
})
