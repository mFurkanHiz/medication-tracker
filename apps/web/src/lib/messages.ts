export const messages = {
  tr: {
    title: 'Medication Tracker', description: 'İlaç düzeni ve stok takibi', eyebrow: 'Sprint 1 · Çevrimdışı temel',
    heading: 'İlaç rutininiz, bağlantıdan bağımsız.', intro: 'Günün planını görün, alındı olarak kaydedin ve stok hareketlerini güvenilir bir geçmişte koruyun.',
    today: 'Bugünün planı', person: 'Deniz', medication: 'Örnek tablet', schedule: '09:00 · 1/2 tablet', due: 'Alınacak',
    stock: 'Kalan stok', stockValue: '7½ tablet', forecast: 'Tahmini 15 gün', sync: 'Eşitleme', syncValue: 'Çevrimdışı hazır', syncHint: 'Tekrarlanan kayıtlar stoğu iki kez düşürmez.',
    audit: 'Değişmez geçmiş', auditHint: 'Alım ve stok hareketleri ayrı olaylar olarak saklanır.', safety: 'Sağlık kararı vermez', safetyHint: 'Uygulama düzenleme ve hatırlatma aracıdır; doz önermez.',
  },
  en: {
    title: 'Medication Tracker', description: 'Medication routine and inventory tracking', eyebrow: 'Sprint 1 · Offline foundation',
    heading: 'Your medication routine, independent of connectivity.', intro: 'See today’s plan, record it as taken, and preserve inventory changes in a reliable history.',
    today: "Today's plan", person: 'Deniz', medication: 'Sample tablet', schedule: '09:00 · 1/2 tablet', due: 'Due',
    stock: 'Stock remaining', stockValue: '7½ tablets', forecast: 'About 15 days', sync: 'Sync', syncValue: 'Offline ready', syncHint: 'Replayed records never decrement stock twice.',
    audit: 'Immutable history', auditHint: 'Administrations and stock movements are stored as separate events.', safety: 'No medical decisions', safetyHint: 'The app organizes and reminds; it does not recommend a dose.',
  },
} as const;
