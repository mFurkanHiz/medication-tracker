/**
 * Every user-visible string on the device, in both locales.
 *
 * `en` is typed as `Record<MessageKey, string>`, so adding a Turkish key without its
 * English counterpart fails `tsc --noEmit` rather than shipping an untranslated screen.
 */
export const tr = {
  appName: 'İlaç Takip',

  // Authentication
  signInTitle: 'Hesabınıza girin',
  signUpTitle: 'Hesap oluşturun',
  email: 'E-posta',
  password: 'Parola',
  confirmPassword: 'Parola tekrarı',
  signIn: 'Giriş yap',
  signUp: 'Kayıt ol',
  signOut: 'Çıkış yap',
  needAccount: 'Hesabınız yok mu? Kayıt olun',
  haveAccount: 'Hesabınız var mı? Giriş yapın',
  passwordHint: 'En az 12 karakter.',
  passwordMismatch: 'Parolalar eşleşmiyor.',
  demoNotice: 'Bu bir portföy uygulamasıdır. Yalnızca sentetik veri girin.',

  // Today
  today: 'Bugün',
  todayEmpty: 'Bugün için planlanmış doz yok.',
  todayEmptyHint: 'Planlarınızı web uygulamasından ekleyebilirsiniz.',
  taken: 'Alındı',
  skip: 'Atla',
  details: 'Ayrıntılar',
  recordedTaken: 'Alındı olarak kaydedildi',
  recordedSkipped: 'Atlandı olarak kaydedildi',
  asNeeded: 'Gerektiğinde',

  // Gün dilimleri ve yemek ilişkisi. Web'de baştan beri vardı, telefonda hiç yoktu:
  // sunucu bunları Bugün satırında gönderiyor, mobil istemci de saklıyordu ama
  // göstermiyordu. 'Tercihen', gerektiğinde alınan bir ilacın tercih edilen zamanını
  // randevu gibi okutmamak için.
  morning: 'Sabah',
  noon: 'Öğle',
  afternoon: 'İkindi',
  evening: 'Akşam',
  night: 'Gece',
  bedtime: 'Yatmadan önce',
  fasting: 'Aç karnına',
  fullStomach: 'Tok karnına',
  beforeFood: 'Yemekten önce',
  withFood: 'Yemekle birlikte',
  afterFood: 'Yemekten sonra',
  preferably: 'tercihen',

  notEnoughStock: 'Stok yetersiz',
  takenFrom: 'Şu kutudan alındı:',
  packageOrdinal: 'Kutu',
  looseStock: 'Kutusuz stok',
  queuedOffline: 'Çevrimdışı kaydedildi, bağlantı gelince eşitlenecek',

  // Advanced dose options
  advancedOptions: 'Gelişmiş',
  whichPackage: 'Hangi kutudan kullandınız?',
  sourceAutomatic: 'Otomatik seç',
  sourceAutomaticHint: 'Açılmış kutu, sonra son kullanma tarihi en yakın olan.',
  sourceSpecific: 'Belirli bir kutu',
  sourceLoose: 'Kutusuz stok',
  sourceUntracked: 'Takip edilmeyen / dış kaynak',
  sourceUntrackedHint: 'Kullanım kaydedilir, stok değişmez. Sayım gerekebilir.',
  amountTaken: 'Alınan miktar',
  amountTakenHint: 'Planlanandan farklıysa girin.',
  outcomeLabel: 'Sonuç',
  partialDose: 'Eksik doz',
  extraDose: 'Ek doz',
  recordDose: 'Kullanımı kaydet',

  // Stock
  stock: 'Stok',
  stockRemaining: 'Kalan',
  packagesLabel: 'kutu',
  lowStock: 'Stok azalıyor',
  outOfStock: 'Stok bitti',

  // Sync
  sync: 'Eşitleme',
  syncNow: 'Şimdi eşitle',
  syncing: 'Eşitleniyor…',
  pendingCount: 'bekleyen kayıt',
  allSynced: 'Tüm kayıtlar eşitlendi',
  syncFailed: 'Eşitleme başarısız. Daha sonra yeniden denenecek.',
  offline: 'Çevrimdışı',
  offlineReady: 'Çevrimdışı hazır',
  lastSynced: 'Son eşitleme',
  never: 'Hiç',

  // Conflicts — never resolved silently
  conflictTitle: 'Çakışma var',
  conflictHint:
    'Bu kayıt sunucuda farklı sonuçlanmış. Sağlık ve stok kayıtları sessizce üzerine yazılmaz; ne yapılacağına siz karar verin.',
  conflictKeepServer: 'Sunucudaki kaydı koru',
  conflictRetry: 'Yeniden gönder',
  conflictDiscard: 'Yerel kaydı sil',
  rejectedTitle: 'Sunucu reddetti',

  // "Birlikte alınmaz" etiketleri (ADR 0016): hanenin kendi sözleri, sunucu eşleştirdi,
  // telefon yalnız gösterir. Uyarıdır, engel değildir; sessizlik güvenlik iddiası değildir.
  doNotTakeWith: '{name} ile birlikte almayınız',
  doNotTakeWithReason: 'Nedeni',
  doNotTakeWithFrom:
    'Bu uyarı hanenin {name} için yazdığı etiketten geliyor; uygulama eksiksiz olduğunu garanti etmez.',

  // Reminders
  reminders: 'Hatırlatıcılar',
  remindersOn: 'Hatırlatıcılar açık',
  remindersOff: 'Hatırlatıcılar kapalı',
  enableReminders: 'Hatırlatıcıları aç',
  reminderPermissionDenied:
    'Bildirim izni verilmedi. Hatırlatıcılar için telefon ayarlarından izin vermeniz gerekir.',
  reminderChannelName: 'İlaç hatırlatıcıları',
  reminderChannelDescription: 'Planlanmış doz zamanlarında hatırlatır.',
  reminderTitle: 'İlaç zamanı',
  reminderBodyPrefix: 'Doz zamanı:',
  reminderRescheduled: 'Hatırlatıcılar yeniden planlandı',
  scheduledCount: 'planlı hatırlatıcı',

  // Stok ve Planlar ekranları: web'in gösterdiğini telefon da gösterir. Düzenleme web'den;
  // ekran bunu söyler, düğmeleri sessizce saklamaz.
  plans: 'Planlar',
  editOnWeb: 'Değişiklikler web uygulamasından yapılır.',
  stockEmpty: 'Henüz ilaç yok.',
  plansEmpty: 'Henüz plan yok.',
  pastPlans: 'Geçmiş planlar',
  expiresOn: 'Son kullanma',
  holder: 'Şu an kimde',
  pinned: 'Etkin kutu',
  coverageInsuranceCovered: 'Sigorta kapsamında',
  coverageSelfPaid: 'Kendi ödemesi',
  stateSealed: 'Kapalı',
  stateOpened: 'Açılmış',
  stateDisposed: 'Atıldı',
  stateLost: 'Kayıp',
  stateArchived: 'Arşivlendi',
  scheduleDaily: 'Her gün',
  scheduleEveryNDays: 'Her {n} günde bir',
  scheduleDayOfMonth: 'Her ayın {d}. günü',
  scheduleEveryNMonths: 'Her {n} ayda bir, ayın {d}. günü',
  statusActive: 'Aktif',
  statusPaused: 'Ara verildi',
  statusEnded: 'Bitti',
  statusUpcoming: 'Başlayacak',
  startsOn: 'Başlangıç',
  endsOn: 'Bitiş',
  weekdayMon: 'Pzt',
  weekdayTue: 'Sal',
  weekdayWed: 'Çar',
  weekdayThu: 'Per',
  weekdayFri: 'Cum',
  weekdaySat: 'Cmt',
  weekdaySun: 'Paz',

  // Kişiler ve Geçmiş: web'in sözleriyle. Geçmişin üstünde bu cihazın henüz
  // eşitlenmemiş dozları da var; web'de böyle bir şey yok çünkü web çevrimdışı çalışmaz.
  people: 'Kişiler',
  peopleEmpty: 'Henüz kişi yok.',
  archivedPeople: 'Arşivdekiler',
  plansLabel: 'plan',
  pausedLabel: 'ara verilmiş',
  history: 'Geçmiş',
  historyEmpty: 'Henüz kayıt yok.',
  historyPending: 'Eşitlenmeyi bekleyen',
  historyCorrections: 'Düzeltmeler',
  historyAdministrations: 'Kullanım kayıtları',
  historyInventory: 'Stok hareketleri',
  pendingLabel: 'Bekliyor',
  correctionApplied: 'Düzeltme',
  untrackedSource: 'Takip edilmeyen kaynak',
  lateBy: 'dk geç',
  earlyBy: 'dk erken',
  outcomeSkipped: 'Atlandı',
  entryAcquire: 'Stok eklendi',
  entryConsume: 'Kullanıldı',
  entryCorrectionReversal: 'Düzeltme iadesi',
  entryCorrectionConsume: 'Düzeltme ile düşüldü',
  entryFound: 'Bulundu',
  entryLoss: 'Kayıp',
  entryDispose: 'Atıldı',
  entryCountAdjustment: 'Sayım düzeltmesi',
  entryPackageTransfer: 'Kutu aktarımı',
  entryManualAdjustment: 'Elle düzeltme',

  // Outbox komutları: web'in sözleriyle. Komut önce telefona yazılır, sonra eşitlenir;
  // sunucu reddederse kullanıcıya gösterilir, sessizce düşürülmez.
  pausePlan: 'Şimdilik ara ver',
  resumePlan: 'Yeniden başla',
  makeActive: 'Etkin kutu yap',
  otherActions: 'Diğer işlemler',
  markLost: 'Kayıp olarak işaretle',
  markDisposed: 'Atıldı olarak işaretle',
  retireConfirm: 'Kutuda kalan miktar stoktan düşülür. Geri alma web uygulamasından yapılır.',
  confirm: 'Onayla',
  addStock: 'Stok ekle',
  sealedBoxes: 'kapalı kutu',
  openedBox: 'açık kutu',
  fullPackagesLabel: 'Kapalı kutu sayısı',
  openedRemainingLabel: 'Açık kutuda kalan',
  capacityLabel: 'Kutu kapasitesi',
  looseLabel: 'Kutusuz miktar',
  stockAddInvalid: 'En az bir kutu ya da kutusuz miktar girin.',
  capacityRequired: 'Kutu kapasitesi gerekli.',
  remainingExceedsCapacity: 'Kalan miktar kapasiteyi aşamaz.',
  queuedCommand: 'Eşitlenmeyi bekliyor',
  queuedCommands: 'Bekleyen işlemler',
  commandRejected: 'Sunucu bu işlemi reddetti',
  dismiss: 'Tamam',
  otherEditsOnWeb: 'Diğer değişiklikler web uygulamasından yapılır.',

  // Generic
  loading: 'Yükleniyor…',
  retry: 'Tekrar dene',
  cancel: 'Vazgeç',
  close: 'Kapat',
  save: 'Kaydet',
  none: 'Yok',
  optional: 'opsiyonel',
  note: 'Not',

  // Errors, keyed by the server's stable refusal codes
  errorGeneric: 'Bir şeyler ters gitti.',
  errorNetwork: 'Sunucuya ulaşılamadı. Kayıt telefonda saklandı.',
  errorUnauthenticated: 'Oturum sona erdi. Yeniden giriş yapın.',
  errorForbidden: 'Bu ev için yetkiniz yok.',
  errorInvalidCredentials: 'E-posta veya parola hatalı.',
  errorAccountExists: 'Bu e-posta ile hesap var.',
  errorInsufficientStock: 'Stok bu doz için yetersiz. Gelişmiş seçeneklerden kaynak seçin.',
  errorChosenSourceInsufficient: 'Seçtiğiniz kaynakta yeterli miktar yok.',
  errorPackageNotEligible: 'Bu kutu kullanıma uygun değil.',
  errorPackageNotFound: 'Kutu bulunamadı.',
  errorPackageNotAvailable: 'Kutu artık kullanımda değil.',
  errorNotFound: 'Kayıt sunucuda bulunamadı; web üzerinde silinmiş olabilir.',

  safetyNotice:
    'Bu uygulama ilaç düzenlemesi ve takibi içindir. Teşhis koymaz, doz önermez.',
} as const;

export type MessageKey = keyof typeof tr;

export const en: Record<MessageKey, string> = {
  appName: 'Medication Tracker',

  signInTitle: 'Sign in to your account',
  signUpTitle: 'Create an account',
  email: 'Email',
  password: 'Password',
  confirmPassword: 'Confirm password',
  signIn: 'Sign in',
  signUp: 'Sign up',
  signOut: 'Sign out',
  needAccount: 'No account? Sign up',
  haveAccount: 'Already have an account? Sign in',
  passwordHint: 'At least 12 characters.',
  passwordMismatch: 'The passwords do not match.',
  demoNotice: 'This is a portfolio application. Enter synthetic data only.',

  today: 'Today',
  todayEmpty: 'Nothing is due today.',
  todayEmptyHint: 'You can add plans from the web application.',
  taken: 'Taken',
  skip: 'Skip',
  details: 'Details',
  recordedTaken: 'Recorded as taken',
  recordedSkipped: 'Recorded as skipped',
  asNeeded: 'As needed',

  morning: 'Morning',
  noon: 'Noon',
  afternoon: 'Afternoon',
  evening: 'Evening',
  night: 'Night',
  bedtime: 'Bedtime',
  fasting: 'On an empty stomach',
  fullStomach: 'On a full stomach',
  beforeFood: 'Before food',
  withFood: 'With food',
  afterFood: 'After food',
  preferably: 'preferably',

  notEnoughStock: 'Not enough stock',
  takenFrom: 'Taken from',
  packageOrdinal: 'Box',
  looseStock: 'Loose stock',
  queuedOffline: 'Saved offline — it will sync when a connection returns',

  advancedOptions: 'Advanced',
  whichPackage: 'Which package did you use?',
  sourceAutomatic: 'Choose automatically',
  sourceAutomaticHint: 'An opened package first, then the earliest expiry.',
  sourceSpecific: 'A specific package',
  sourceLoose: 'Loose stock',
  sourceUntracked: 'Untracked / external',
  sourceUntrackedHint: 'The dose is recorded and stock is untouched. A count may be needed.',
  amountTaken: 'Amount taken',
  amountTakenHint: 'Enter it if it differed from the plan.',
  outcomeLabel: 'Outcome',
  partialDose: 'Partial dose',
  extraDose: 'Extra dose',
  recordDose: 'Record dose',

  stock: 'Stock',
  stockRemaining: 'Remaining',
  packagesLabel: 'packages',
  lowStock: 'Running low',
  outOfStock: 'Out of stock',

  sync: 'Sync',
  syncNow: 'Sync now',
  syncing: 'Syncing…',
  pendingCount: 'pending records',
  allSynced: 'Everything is synced',
  syncFailed: 'Sync failed. It will retry later.',
  offline: 'Offline',
  offlineReady: 'Offline ready',
  lastSynced: 'Last synced',
  never: 'Never',

  conflictTitle: 'Conflict',
  conflictHint:
    'The server recorded this differently. Health and inventory records are never silently overwritten — you decide what happens.',
  conflictKeepServer: 'Keep the server record',
  conflictRetry: 'Send again',
  conflictDiscard: 'Discard the local record',
  rejectedTitle: 'The server refused this',

  doNotTakeWith: 'Do not take with {name}',
  doNotTakeWithReason: 'Reason',
  doNotTakeWithFrom: "From the household's own tag on {name}; the app does not vouch for completeness.",

  reminders: 'Reminders',
  remindersOn: 'Reminders are on',
  remindersOff: 'Reminders are off',
  enableReminders: 'Turn reminders on',
  reminderPermissionDenied:
    'Notification permission was not granted. Reminders need it, from the phone settings.',
  reminderChannelName: 'Medication reminders',
  reminderChannelDescription: 'Reminds you at scheduled dose times.',
  reminderTitle: 'Medication time',
  reminderBodyPrefix: 'Dose due:',
  reminderRescheduled: 'Reminders were rescheduled',
  scheduledCount: 'scheduled reminders',

  plans: 'Plans',
  editOnWeb: 'Changes are made in the web app.',
  stockEmpty: 'No medications yet.',
  plansEmpty: 'No plans yet.',
  pastPlans: 'Past plans',
  expiresOn: 'Expires',
  holder: 'Currently held by',
  pinned: 'Active box',
  coverageInsuranceCovered: 'Insurance-covered',
  coverageSelfPaid: 'Self-paid',
  stateSealed: 'Sealed',
  stateOpened: 'Opened',
  stateDisposed: 'Disposed',
  stateLost: 'Lost',
  stateArchived: 'Archived',
  scheduleDaily: 'Every day',
  scheduleEveryNDays: 'Every {n} days',
  scheduleDayOfMonth: 'Day {d} of every month',
  scheduleEveryNMonths: 'Every {n} months, on day {d}',
  statusActive: 'Active',
  statusPaused: 'Paused',
  statusEnded: 'Ended',
  statusUpcoming: 'Starts later',
  startsOn: 'Starts',
  endsOn: 'Ends',
  weekdayMon: 'Mon',
  weekdayTue: 'Tue',
  weekdayWed: 'Wed',
  weekdayThu: 'Thu',
  weekdayFri: 'Fri',
  weekdaySat: 'Sat',
  weekdaySun: 'Sun',

  people: 'People',
  peopleEmpty: 'No people yet.',
  archivedPeople: 'Archived',
  plansLabel: 'plans',
  pausedLabel: 'paused',
  history: 'History',
  historyEmpty: 'Nothing recorded yet.',
  historyPending: 'Waiting to sync',
  historyCorrections: 'Corrections',
  historyAdministrations: 'Recorded doses',
  historyInventory: 'Stock movements',
  pendingLabel: 'Pending',
  correctionApplied: 'Correction',
  untrackedSource: 'Untracked source',
  lateBy: 'min late',
  earlyBy: 'min early',
  outcomeSkipped: 'Skipped',
  entryAcquire: 'Stock added',
  entryConsume: 'Used',
  entryCorrectionReversal: 'Correction credit',
  entryCorrectionConsume: 'Charged by correction',
  entryFound: 'Found',
  entryLoss: 'Lost',
  entryDispose: 'Disposed',
  entryCountAdjustment: 'Count adjustment',
  entryPackageTransfer: 'Package transfer',
  entryManualAdjustment: 'Manual adjustment',

  pausePlan: 'Pause for now',
  resumePlan: 'Start again',
  makeActive: 'Make this the active box',
  otherActions: 'Other actions',
  markLost: 'Mark as lost',
  markDisposed: 'Mark as disposed',
  retireConfirm: 'What is left in the box leaves the stock. Undoing this is done in the web app.',
  confirm: 'Confirm',
  addStock: 'Add stock',
  sealedBoxes: 'sealed boxes',
  openedBox: 'opened box',
  fullPackagesLabel: 'Sealed boxes',
  openedRemainingLabel: 'Left in an opened box',
  capacityLabel: 'Box capacity',
  looseLabel: 'Loose amount',
  stockAddInvalid: 'Enter at least one box or a loose amount.',
  capacityRequired: 'The box capacity is needed.',
  remainingExceedsCapacity: 'What is left cannot exceed the capacity.',
  queuedCommand: 'Waiting to sync',
  queuedCommands: 'Pending actions',
  commandRejected: 'The server refused this action',
  dismiss: 'OK',
  otherEditsOnWeb: 'Other changes are made in the web app.',

  loading: 'Loading…',
  retry: 'Try again',
  cancel: 'Cancel',
  close: 'Close',
  save: 'Save',
  none: 'None',
  optional: 'optional',
  note: 'Note',

  errorGeneric: 'Something went wrong.',
  errorNetwork: 'Could not reach the server. The record was saved on this phone.',
  errorUnauthenticated: 'Your session ended. Please sign in again.',
  errorForbidden: 'You do not have access to this household.',
  errorInvalidCredentials: 'That email or password is not correct.',
  errorAccountExists: 'An account already exists for this email.',
  errorInsufficientStock: 'There is not enough stock for this dose. Choose a source under advanced.',
  errorChosenSourceInsufficient: 'The source you chose does not hold enough.',
  errorPackageNotEligible: 'That package cannot be used.',
  errorPackageNotFound: 'Package not found.',
  errorPackageNotAvailable: 'The box is no longer in use.',
  errorNotFound: 'Not found on the server; it may have been deleted on the web.',

  safetyNotice:
    'This application organises and tracks medication. It does not diagnose or recommend a dose.',
};

export const dictionaries = { tr, en } as const;

export type Locale = keyof typeof dictionaries;

export const LOCALES: readonly Locale[] = ['tr', 'en'];

export type Translate = (key: MessageKey) => string;

/** Maps a server refusal code onto a translated message. */
/**
 * Translation keys for the enum names the server returns.
 *
 * Mirrors the web client's `enumKey`. The names are what the API sends, and they are
 * persisted as text server-side, so a rename there has to be reflected in both clients.
 */
export function enumKey(value: string | null | undefined): MessageKey | null {
  if (!value) {
    return null;
  }

  const map: Record<string, MessageKey> = {
    Morning: 'morning',
    Noon: 'noon',
    Afternoon: 'afternoon',
    Evening: 'evening',
    Night: 'night',
    Bedtime: 'bedtime',
    Fasting: 'fasting',
    FullStomach: 'fullStomach',
    BeforeFood: 'beforeFood',
    WithFood: 'withFood',
    AfterFood: 'afterFood',
  };

  return map[value] ?? null;
}

export function errorKey(code: string): MessageKey {
  const map: Record<string, MessageKey> = {
    unauthenticated: 'errorUnauthenticated',
    forbidden: 'errorForbidden',
    invalid_credentials: 'errorInvalidCredentials',
    account_exists: 'errorAccountExists',
    insufficient_stock: 'errorInsufficientStock',
    chosen_source_insufficient: 'errorChosenSourceInsufficient',
    package_not_eligible: 'errorPackageNotEligible',
    package_not_found: 'errorPackageNotFound',
    package_not_specified: 'errorPackageNotFound',
    package_not_available: 'errorPackageNotAvailable',
    not_found: 'errorNotFound',
  };

  return map[code] ?? 'errorGeneric';
}
