export const messages = {
  tr: {
    eyebrow: 'Bugünün planı', title: 'İlaç takibi, çevrimdışı da yanında.',
    description: 'Kayıtlar önce bu telefona yazılır. Bağlantı geldiğinde güvenle eşitlenir.',
    createDemo: 'Örnek planı oluştur', due: 'Alınacak', taken: 'Alındı', skipped: 'Atlandı', markTaken: 'Alındı işaretle', markSkipped: 'Atlandı işaretle',
    stock: 'Kalan stok', pending: 'Bekleyen eşitleme', forecast: 'Tahmini süre', days: 'gün', empty: 'Bugün için henüz bir plan yok.',
    exact: 'Kesin miktar', offline: 'Çevrimdışı hazır', loading: 'Yerel kayıtlar hazırlanıyor…',
    syncNow: 'Şimdi eşitle', syncing: 'Eşitleniyor…', syncError: 'Eşitleme daha sonra yeniden denenecek.',
    samplePerson: 'Deniz', sampleMedication: 'Örnek tablet', tablet: 'tablet',
  },
  en: {
    eyebrow: "Today's plan", title: 'Medication tracking that stays with you offline.',
    description: 'Changes are saved on this phone first and sync safely when a connection returns.',
    createDemo: 'Create sample plan', due: 'Due', taken: 'Taken', skipped: 'Skipped', markTaken: 'Mark as taken', markSkipped: 'Mark as skipped',
    stock: 'Stock remaining', pending: 'Pending sync', forecast: 'Estimated supply', days: 'days', empty: 'There is no plan for today yet.',
    exact: 'Exact quantity', offline: 'Offline ready', loading: 'Preparing local records…',
    syncNow: 'Sync now', syncing: 'Syncing…', syncError: 'Sync will retry later.',
    samplePerson: 'Deniz', sampleMedication: 'Sample tablet', tablet: 'tablet',
  },
} as const;

export type Locale = keyof typeof messages;
