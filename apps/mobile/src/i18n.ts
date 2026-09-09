export const messages = {
  tr: {
    eyebrow: 'Bugünün planı', title: 'İlaç takibi, çevrimdışı da yanında.',
    description: 'Kayıtlar önce bu telefona yazılır. Bağlantı geldiğinde güvenle eşitlenir.',
    createDemo: 'Örnek planı oluştur', due: 'Alınacak', taken: 'Alındı', markTaken: 'Alındı işaretle',
    stock: 'Kalan stok', pending: 'Bekleyen eşitleme', empty: 'Bugün için henüz bir plan yok.',
    exact: 'Kesin miktar', offline: 'Çevrimdışı hazır', loading: 'Yerel kayıtlar hazırlanıyor…',
    samplePerson: 'Deniz', sampleMedication: 'Örnek tablet', tablet: 'tablet',
  },
  en: {
    eyebrow: "Today's plan", title: 'Medication tracking that stays with you offline.',
    description: 'Changes are saved on this phone first and sync safely when a connection returns.',
    createDemo: 'Create sample plan', due: 'Due', taken: 'Taken', markTaken: 'Mark as taken',
    stock: 'Stock remaining', pending: 'Pending sync', empty: 'There is no plan for today yet.',
    exact: 'Exact quantity', offline: 'Offline ready', loading: 'Preparing local records…',
    samplePerson: 'Deniz', sampleMedication: 'Sample tablet', tablet: 'tablet',
  },
} as const;

export type Locale = keyof typeof messages;
