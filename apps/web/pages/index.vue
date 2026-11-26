<template>
  <main class="home">
    <h1>{{ $t('app.name') }}</h1>
    <p>{{ $t('app.tagline') }}</p>
  </main>
</template>

<style scoped>
.home { padding: 24px 16px; }
h1 { margin: 0 0 8px; }
p { color: var(--muted); margin: 0; }
</style>
