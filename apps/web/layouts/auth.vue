<template>
  <div class="auth-shell">
    <main class="auth-content">
      <slot />
    </main>
  </div>
</template>

<style scoped>
.auth-shell {
  min-height: 100dvh;
  display: grid;
  align-items: start;
}
.auth-content {
  width: 100%;
  max-width: 440px;
  margin: 0 auto;
  padding: var(--space-5) var(--space-4) var(--space-6);
}
@media (min-height: 760px) {
  .auth-content { padding-top: 8dvh; }
}
</style>
