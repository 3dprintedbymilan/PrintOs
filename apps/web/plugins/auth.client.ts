export default defineNuxtPlugin(() => {
  const auth = useAuthStore()
  auth.hydrate()
  if (auth.isSignedIn) void auth.syncLanguage()
})
