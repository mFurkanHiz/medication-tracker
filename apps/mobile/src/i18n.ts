export const messages = {
  tr: {
    addMedication: 'İlaç ve plan ekle', cancel: 'Vazgeç', save: 'Kaydet', personName: 'Kişinin adı', medicationName: 'İlacın adı', stockInput: 'Stok (tablet, örneğin 30)', doseInput: 'Doz (tablet, örneğin 1/2)', timeInput: 'Saat (SS:DD)', invalidPlan: 'Adları, pozitif miktarları ve saati kontrol edin.',
    login: 'Giriş yap', register: 'Hesap oluştur', email: 'E-posta', password: 'Parola (en az 12 karakter)', logout: 'Çıkış yap', loginError: 'Giriş yapılamadı. Bilgilerinizi ve bağlantınızı kontrol edin.',
    eyebrow: 'Bugünün planı', title: 'İlaç takibi, çevrimdışı da yanında.',
    description: 'Kayıtlar önce bu telefona yazılır. Bağlantı geldiğinde güvenle eşitlenir.',
    createDemo: 'Örnek planı oluştur', due: 'Alınacak', taken: 'Alındı', skipped: 'Atlandı', markTaken: 'Alındı işaretle', markSkipped: 'Atlandı işaretle',
    stock: 'Kalan stok', pending: 'Bekleyen eşitleme', forecast: 'Tahmini süre', days: 'gün', empty: 'Bugün için henüz bir plan yok.',
    exact: 'Kesin miktar', offline: 'Çevrimdışı hazır', loading: 'Yerel kayıtlar hazırlanıyor…',
    syncNow: 'Şimdi eşitle', syncing: 'Eşitleniyor…', syncError: 'Eşitleme daha sonra yeniden denenecek.',
    samplePerson: 'Deniz', sampleMedication: 'Örnek tablet', tablet: 'tablet',
  },
  en: {
    addMedication: 'Add medication and plan', cancel: 'Cancel', save: 'Save', personName: 'Person’s name', medicationName: 'Medication name', stockInput: 'Stock (tablets, e.g. 30)', doseInput: 'Dose (tablets, e.g. 1/2)', timeInput: 'Time (HH:MM)', invalidPlan: 'Check the names, positive quantities and time.',
    login: 'Sign in', register: 'Create account', email: 'Email', password: 'Password (at least 12 characters)', logout: 'Sign out', loginError: 'Unable to sign in. Check your credentials and connection.',
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
