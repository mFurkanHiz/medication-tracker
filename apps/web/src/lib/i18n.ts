'use client';

import { createContext, useContext } from 'react';

import type { CautionNotes } from './types';

/**
 * Every user-visible string lives here, in both locales.
 *
 * `English` is typed as `Record<MessageKey, string>`, so adding a Turkish key without
 * its English counterpart fails the build rather than shipping an untranslated screen.
 */
export const tr = {
  appName: 'İlaç Takip',
  appDescription: 'Ev ilaç düzeni, kutu bazlı stok ve denetlenebilir kullanım geçmişi',

  // Authentication
  signInTitle: 'Hesabınıza girin',
  signUpTitle: 'Hesap oluşturun',
  email: 'E-posta',
  password: 'Şifre',
  confirmPassword: 'Şifreyi doğrula',
  signIn: 'Giriş yap',
  signUp: 'Kayıt ol',
  signOut: 'Çıkış yap',
  needAccount: 'Hesabınız yok mu? Kayıt olun',
  haveAccount: 'Hesabınız var mı? Giriş yapın',
  passwordHint: 'En az 12 karakter.',
  demoNotice: 'Bu herkese açık bir portföy uygulamasıdır. Yalnızca sentetik veri girin.',

  // Navigation
  today: 'Bugün',
  inventory: 'İlaçlar',
  plans: 'Planlar',
  people: 'Kişiler',
  history: 'Geçmiş',

  // Today
  todayEmpty: 'Bugün için planlanmış doz yok.',
  todayEmptyHint: 'Bir plan ekleyin; bugünün dozları burada görünür.',
  taken: 'Alındı',
  skip: 'Atla',
  recordedTaken: 'Alındı olarak kaydedildi',
  recordedSkipped: 'Atlandı',
  details: 'Ayrıntılar',
  asNeeded: 'Gerektiğinde',
  notEnoughStock: 'Stok yetersiz',
  takenFrom: 'Şu kutudan alındı:',
  looseStock: 'Kutusuz stok',
  untrackedSource: 'Takip edilmeyen kaynak',

  // Advanced dose options
  advancedOptions: 'Gelişmiş seçenekler',
  whichPackage: 'Hangi kutudan kullandınız?',
  sourceAutomatic: 'Otomatik seç',
  sourceAutomaticHint: 'Açılmış kutu, sonra son kullanma tarihi en yakın olan.',
  sourceSpecific: 'Belirli bir kutu',
  sourceLoose: 'Kutusuz stok',
  sourceUntracked: 'Takip edilmeyen / dış kaynak',
  sourceUntrackedHint: 'Kullanım kaydedilir, stok değişmez. Sayım gerekebilir.',
  amountTaken: 'Alınan miktar',
  amountTakenHint: 'Planlanandan farklıysa girin.',
  partialDose: 'Eksik doz',
  extraDose: 'Ek doz',
  recordDose: 'Kullanımı kaydet',
  recordExtraDose: 'Ek doz kaydet',

  // Correction
  correctSource: 'Stok kaynağını düzelt',
  correctSourceHint:
    'Sistem yanlış kutudan düşmüşse düzeltin. Toplam miktar değişmez; geçmiş silinmez.',
  correctedFrom: 'Düzeltildi:',
  correctionReason: 'Neden (opsiyonel)',
  applyCorrection: 'Düzeltmeyi uygula',
  correctionApplied: 'Stok kaynağı düzeltildi',
  supersededAllocation: 'Geçersiz kılındı',

  // Inventory
  addMedication: 'İlaç tanımla',
  editMedication: 'İlaç tanımını düzenle',
  addStock: 'Stok ekle',
  inventoryEmpty: 'Henüz ilaç tanımlanmadı.',
  inventoryEmptyHint: 'Önce bir ilaç tanımlayın, sonra fiziksel kutularını ekleyin.',
  packagesLabel: 'kutu',
  packageOrdinal: 'Kutu',
  full: 'Tam',
  opened: 'Açılmış',
  empty: 'Boş',
  disposed: 'Atıldı',
  lost: 'Kayıp',
  archived: 'Arşivlendi',
  pinned: 'Etkin kutu',
  pin: 'Etkin kutu yap',
  unpin: 'Etkin kutudan çıkar',
  retire: 'Kullanımdan çıkar',
  retireDisposed: 'Atıldı olarak işaretle',
  retireLost: 'Kayıp olarak işaretle',
  onLoan: 'Ödünç verildi',
  lend: 'Ödünç ver',
  returnLoan: 'Geri alındı',
  owner: 'Sahibi',
  holder: 'Şu an kimde',
  unassigned: 'Atanmamış',
  expiresOn: 'Son kullanma',
  acquiredOn: 'Edinme tarihi',
  lotNumber: 'Lot numarası',
  storageLocation: 'Saklama yeri',
  note: 'Not',
  showPackages: 'Kutuları göster',
  hidePackages: 'Kutuları gizle',
  archiveMedication: 'İlacı arşivle',
  archiveKeeps:
    'Stok, kutular ve geçmiş kayıtların hiçbiri silinmez. İlaç listeden kalkar, Bugün ekranında doz çıkmaz ve sayımda görünmez.',
  archiveStopsPlansBefore: 'Bu ilaç için duran',
  archiveStopsPlansAfter:
    'kullanım planına ara verilir. Planlar silinmez: ilacı geri getirdikten sonra kaldıkları yerden devam ettirebilirsiniz.',
  archiveNoPlans: 'Bu ilaç için duran bir kullanım planı yok.',
  restorePerson: 'Geri getir',
  archivePersonTitle: 'Kişiyi arşivle',
  archivePersonKeeps:
    'Geçmiş kayıtların hiçbiri silinmez. Kişi listelerden kalkar ve Bugün ekranında dozu çıkmaz.',
  archivePersonPausesPlansBefore: 'Bu kişinin',
  archivePersonPausesPlansAfter:
    'kullanım planına ara verilir. Planlar silinmez: kişiyi geri getirdikten sonra devam ettirebilirsiniz.',
  archivePersonNoPlans: 'Bu kişi için duran bir kullanım planı yok.',
  archiveReversible: 'İlacın kendisi, stoğuyla birlikte, aşağıdaki “Arşivlendi” bölümünden geri getirilebilir.',
  restoreMedication: 'Arşivden çıkar',

  // Definition form
  medicationName: 'İlaç adı',
  strength: 'Doz bilgisi',
  strengthPlaceholder: 'örn. 500 mg',
  brand: 'Marka',
  manufacturer: 'Üretici',
  form: 'Form',
  unit: 'Birim',
  activeIngredients: 'Etken maddeler',
  activeIngredientsHint: 'Virgülle ayırın.',
  defaultPackageSize: 'Varsayılan kutu kapasitesi',
  defaultPackageSizeHint:
    'Yeni kutular için ön değer. Bunu değiştirmek mevcut kutuları değiştirmez.',
  category: 'Kategori',
  tags: 'Etiketler',
  notes: 'Notlar',

  // Güvenli kullanım notları. Hanenin kendi yazdığı notlar; uygulama bunları saklar ve
  // gösterir, kendisi hiçbir değerlendirme yapmaz. Etiketlerde "etkileşim" sözcüğü
  // bilerek kullanılmıyor: o sözcük, yazılımın bir yargıya vardığını ima eder.
  cautionNotes: 'Güvenli kullanım notları',
  cautionNotesHint:
    'Doktorunuzun, eczacınızın ya da prospektüsün söylediklerini kendi sözlerinizle '
    + 'yazın. Uygulama bu notları saklar ve ilacı alacağınız yerde gösterir; kendisi '
    + 'hiçbir değerlendirme yapmaz.',
  cautionNotesOwn: 'Hanenin kendi notu',
  cautionDoNotTakeWith: 'Birlikte alınmaması gerekenler',
  cautionDoNotTakeWithHint: 'Örneğin başka bir ilaç ya da takviye.',
  cautionFoodsToAvoid: 'Kaçınılacak yiyecek ve içecekler',
  cautionThingsToDo: 'Yapılması gerekenler',
  cautionThingsToDoHint: 'Örneğin "bir bardak dolusu su ile al".',
  cautionThingsToAvoid: 'Yapılmaması gerekenler',
  cautionThingsToAvoidHint: 'Örneğin "aldıktan sonra yarım saat uzanma".',
  cautionWarning: 'Diğer uyarılar',

  // "En az ara" alanı artık gerçekten bir şey yapıyor: erken olduğunu söylüyor. Ama
  // ENGELLEMİYOR. Gerçekten alınmış bir dozu kaydetmeyi reddetmek defteri yalancı yapar,
  // ve insanı gerçeği kaydettiği için cezalandırmak ona kaydetmeyi bırakmayı öğretir.
  tooSoon: 'Kendi notunuza göre henüz erken',
  tooSoonStillRecordable: 'Uygulama engellemiyor — gerçekte ne olduysa onu kaydedin.',
  lastTakenLabel: 'Son alınan',
  earliestNextLabel: 'En erken',
  minimumGapShort: 'En az ara',
  minutesShort: 'dk',

  // Add stock form
  fullPackageCount: 'Tam kutu sayısı',
  packageCapacity: 'Kutu kapasitesi',
  openedPackage: 'Açılmış kutu',
  addOpenedPackage: '+ Açılmış kutu ekle',
  remainingInPackage: 'Kalan miktar',
  removeRow: 'Kaldır',
  looseAmount: 'Kutusuz miktar',
  assignTo: 'Kişiye ata',
  stockPreview: 'Eklenecek:',
  stockPreviewPackages: 'kutu',
  addStockAgainHint:
    'Sonradan da ekleyebilirsiniz — yeni bir kutu aldığınızda ya da kayıp bir kutuyu bulduğunuzda bu formu yeniden açmanız yeter.',

  // Plans
  addPlan: 'Plan ekle',
  editPlan: 'Planı düzenle',
  plansEmpty: 'Henüz kullanım planı yok.',
  plansEmptyHint: 'Kimin hangi ilacı nasıl kullandığını buradan tanımlayın.',
  person: 'Kişi',
  medication: 'İlaç',
  dose: 'Doz',
  schedule: 'Zamanlama',
  scheduleDaily: 'Her gün',
  scheduleWeekdays: 'Seçili günler',
  scheduleInterval: 'Her N günde',
  scheduleAsNeeded: 'Gerektiğinde',
  intervalDays: 'Gün aralığı',
  exactTime: 'Saat',
  whenInDay: 'Ne zaman',
  useExactTime: 'Kesin saat',
  preferredTime: 'Tercih edilen zaman',
  preferredTimeHint:
    'Sabit bir saati yoktur ama bir tercihiniz olabilir. Burası yalnızca not; takvime doz eklenmez ve almadığınız gün kaçırılmış sayılmaz.',
  noTimePreference: 'Farketmez',
  preferably: 'tercihen',
  asNeededExplainer:
    'Gerektiğinde alınan ilacın sabit saati yoktur: takvime doz eklenmez ve almadığınız gün kaçırılmış sayılmaz. İhtiyaç duyduğunuzda alır, Bugün ekranından kaydedersiniz. Tercih ettiğiniz gün dilimini ve yemek ilişkisini yine de buraya yazabilirsiniz.',
  mealRelation: 'Yemek ilişkisi',
  mealRelationHint:
    'Doktorunuzun ya da prospektüsün söylediğini olduğu gibi not edin. Uygulama bunu yorumlamaz, yalnızca ilacı alırken size gösterir.',
  minimumInterval: 'En az ara (dakika)',
  effectiveFrom: 'Başlangıç',
  effectiveTo: 'Bitiş',
  instructions: 'Talimat notu',
  planVersionNote: 'Düzenleme yeni bir sürüm oluşturur; geçmiş kayıtlar değişmez.',
  deletePlan: 'Planı sonlandır',
  pausePlan: 'Şimdilik ara ver',
  resumePlan: 'Yeniden başla',
  planPaused: 'Ara verildi',
  planMedicationArchived: 'Devam ettirmek için önce ilacı arşivden geri getirin.',
  planPersonArchived: 'Devam ettirmek için önce kişiyi arşivden geri getirin.',
  medicationHasPausedPlan:
    'Bu ilaç için ara verilmiş bir plan var. Yeniden almaya başladıysanız buradan devam ettirebilirsiniz.',

  monday: 'Pzt',
  tuesday: 'Sal',
  wednesday: 'Çar',
  thursday: 'Per',
  friday: 'Cum',
  saturday: 'Cmt',
  sunday: 'Paz',

  morning: 'Sabah',
  noon: 'Öğle',
  afternoon: 'İkindi',
  evening: 'Akşam',
  night: 'Gece',
  bedtime: 'Yatmadan önce',

  fasting: 'Aç karnına',
  fullStomach: 'Tok karnına',
  beforeFood: 'Yemekten önce',
  withFood: 'Yemekle',
  afterFood: 'Yemekten sonra',

  // People
  addPerson: 'Kişi ekle',
  personName: 'Ad',
  peopleEmpty: 'Henüz kişi eklenmedi.',
  peopleEmptyHint: 'İlaç kullanan kişileri ekleyin.',
  archivePerson: 'Kişiyi arşivle',
  rename: 'Adı değiştir',

  // Refill
  refill: 'Temin',
  refillSettings: 'Temin ayarları',
  lowStockThreshold: 'Az stok eşiği',
  lowStockDays: 'Az stok uyarısı (gün)',
  nextEligibleRefill: 'Resmî temin tarihi',
  nextEligibleRefillHint: 'Reçetenin yenilenebileceği en erken tarih. Stoktan bağımsızdır.',
  lowStockWarning: 'Stok azalıyor',
  depletionOn: 'Tahmini bitiş',
  daysRemaining: 'gün kaldı',
  refillGapWarning: 'Temin açığı',
  refillGapDetail: 'gün ilaçsız kalma riski',
  alreadyDepleted: 'Stok bitti',
  notForecastable: 'Tahmin için planlı kullanım gerekir',

  // History
  historyEmpty: 'Henüz kayıt yok.',
  historyInventory: 'Stok hareketleri',
  historyAdministrations: 'Kullanım kayıtları',
  historyCorrections: 'Düzeltmeler',
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
  lateBy: 'dakika gecikmeli',
  earlyBy: 'dakika erken',

  // Generic
  save: 'Kaydet',
  cancel: 'Vazgeç',
  close: 'Kapat',
  optional: 'opsiyonel',
  loading: 'Yükleniyor…',
  retry: 'Tekrar dene',
  none: 'Yok',
  total: 'Toplam',
  remaining: 'Kalan',

  // Errors, keyed by the server's stable refusal codes
  errorGeneric: 'Bir şeyler ters gitti. Tekrar deneyin.',
  errorNetwork: 'Sunucuya ulaşılamadı.',
  errorUnauthenticated: 'Oturum sona erdi. Yeniden giriş yapın.',
  errorForbidden: 'Bu ev için yetkiniz yok.',
  errorInsufficientStock: 'Stok bu doz için yetersiz. Gelişmiş seçeneklerden kaynak seçin.',
  errorChosenSourceInsufficient: 'Seçtiğiniz kaynakta bu doz için yeterli miktar yok.',
  errorPackageNotEligible: 'Bu kutu kullanıma uygun değil.',
  errorPackageNotFound: 'Kutu bulunamadı.',
  errorTargetInsufficientStock:
    'Hedef kutuda yeterli miktar yok. Bu bir sayım konusu; düzeltme eksi stok oluşturamaz.',
  errorTargetNotEligible: 'Hedef kutu kullanıma uygun değil.',
  errorSameSource: 'Kaynak aynı; düzeltecek bir şey yok.',
  errorAllocationNotActive: 'Bu kayıt daha önce düzeltilmiş.',
  errorAdministrationUntracked: 'Bu kullanım stoktan düşmedi; düzeltilecek bir kaynak yok.',
  errorAccountExists: 'Bu e-posta ile hesap var.',
  errorPasswordMismatch: 'Şifreler eşleşmiyor.',
  errorInvalidCredentials: 'E-posta veya şifre hatalı.',
  errorStaleRevision: 'Daha yeni bir sayım var. Önce onu görüntüleyin.',
  errorPackageOnLoan: 'Kutu ödünç verilmiş durumda.',

  // Reports
  reports: 'Raporlar',
  reportsAdherence: 'Kullanım özeti',
  reportsInventory: 'Stok özeti',
  reportPeriod: 'Dönem',
  reportPeriodLast7: 'Son 7 gün',
  reportPeriodLast30: 'Son 30 gün',
  reportPeriodLast90: 'Son 90 gün',
  reportPeriodThisMonth: 'Bu ay',
  reportEmpty: 'Bu dönemde kayıtlı doz veya planlanmış doz yok.',
  reportEmptyHint: 'Bir plan ekleyin veya bir doz kaydedin; özet burada görünür.',
  reportHouseholdTotal: 'Ev toplamı',
  reportPlanned: 'Planlanan',
  reportTakenOnSchedule: 'Planına göre alınan',
  reportMissed: 'Kayıtsız',
  reportSkipped: 'Atlanan',
  reportPartial: 'Kısmi',
  reportExtra: 'Ek doz',
  reportRecorded: 'Kayıtlı doz',
  reportNoScheduledDoses: 'Planlanmış doz yok',
  reportDescriptiveNotice:
    'Bu sayılar yalnızca planlanan ve kaydedilen dozları gösterir. Değerlendirme veya öneri içermez.',
  reportUnknownTimeZone:
    'Bir planın saat dilimi bu sunucuda tanımlı değil, bu yüzden dozları sayılmadı.',
  reportStockRemaining: 'Kalan',
  reportDepletion: 'Bitiş tahmini',
  reportDaysLeft: 'gün',
  reportNotForecastable: 'Tahmin edilemez',
  reportLowStock: 'Stok az',
  reportRefillGap: 'Yenileme boşluğu',
  reportRefillGapDays: 'gün açık kalıyor',
  reportLowStockSummary: 'stok az',
  reportRefillGapSummary: 'yenileme boşluğu',

  // Export
  export: 'Veri indir',
  exportTitle: 'Verilerinizi indirin',
  exportDescription:
    'Evinizin ilaç geçmişini bir dosya olarak indirin: ilaçlar, kutular, stok hareketleri, planlar ve tüm kullanım kayıtları.',
  exportButton: 'JSON dosyası indir',
  exportExcludesNotice: 'Dosya hesap bilgisi, şifre veya oturum verisi içermez.',
  exportPreparing: 'Hazırlanıyor…',
  exportDone: 'Dosya indirildi.',

  // Counting
  counting: 'Sayım',
  countingTitle: 'Elinizdekini sayın',
  countingDescription:
    'Saydığınız miktarı yazın. Yalnızca doldurduğunuz satırlar kaydedilir; boş bıraktıklarınıza dokunulmaz.',
  countingExpected: 'Kayıttaki',
  countingObserved: 'Saydığınız',
  countingObservedHint: 'Örnek: 12, 7½, 3/2',
  countingSubmit: 'Sayımı kaydet',
  countingSubmitting: 'Kaydediliyor…',
  countingNote: 'Not',
  countingNoteOptional: 'isteğe bağlı',
  countingNothingEntered: 'Kaydetmek için en az bir miktar girin.',
  countingInvalidAmount: 'Bu miktar okunamadı.',
  countingAccepted: 'Sayım kaydedildi.',
  countingAdvanced: 'Gelişmiş: kutu bazlı sayım',
  countingByPackage: 'Kutu kutu say',
  countingByPackageHint:
    'Kutuların tek tek sayısını girin. İlacın tamamı yerine yalnızca seçtiğiniz kutular düzeltilir.',
  countingWholeMedication: 'İlacın tamamını say',
  countingEmpty: 'Sayılacak ilaç yok.',
  countingEmptyHint: 'Önce bir ilaç tanımlayın.',

  // Count history and revisions
  countingHistory: 'Geçmiş sayımlar',
  countingHistoryEmpty: 'Henüz sayım yapılmadı.',
  countingRevision: 'Düzeltme',
  countingRevisionNumber: 'revizyon',
  countingCorrect: 'Düzelt',
  countingCorrecting: 'Bu sayımın düzeltmesini giriyorsunuz',
  countingCorrectingHint:
    'Kaydedilmiş sayım değiştirilmez; düzeltmeniz ona bağlı yeni bir revizyon olarak eklenir.',
  countingCancelCorrection: 'Düzeltmeden vazgeç',
  countingSuperseded: 'Daha yeni bir revizyon var',
  countingMatched: 'Sayım tuttu',
  countingDelta: 'Fark',

  // Safety
  safetyNotice:
    'Bu uygulama ilaç düzenlemesi ve takibi içindir. Teşhis koymaz, doz önermez ve ilaç etkileşimi değerlendirmez.',
} as const;

export type MessageKey = keyof typeof tr;

export const en: Record<MessageKey, string> = {
  appName: 'Medication Tracker',
  appDescription: 'Household medication organisation, package-level stock, and an auditable history',

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
  demoNotice: 'This is a public portfolio application. Enter synthetic data only.',

  today: 'Today',
  inventory: 'Medications',
  plans: 'Plans',
  people: 'People',
  history: 'History',

  todayEmpty: 'Nothing is due today.',
  todayEmptyHint: 'Add a plan and today’s doses will appear here.',
  taken: 'Taken',
  skip: 'Skip',
  recordedTaken: 'Recorded as taken',
  recordedSkipped: 'Skipped',
  details: 'Details',
  asNeeded: 'As needed',
  notEnoughStock: 'Not enough stock',
  takenFrom: 'Taken from',
  looseStock: 'Loose stock',
  untrackedSource: 'Untracked source',

  advancedOptions: 'Advanced options',
  whichPackage: 'Which package did you use?',
  sourceAutomatic: 'Choose automatically',
  sourceAutomaticHint: 'An opened package first, then the earliest expiry.',
  sourceSpecific: 'A specific package',
  sourceLoose: 'Loose stock',
  sourceUntracked: 'Untracked / external',
  sourceUntrackedHint: 'The dose is recorded and stock is untouched. A count may be needed.',
  amountTaken: 'Amount taken',
  amountTakenHint: 'Enter it if it differed from the plan.',
  partialDose: 'Partial dose',
  extraDose: 'Extra dose',
  recordDose: 'Record dose',
  recordExtraDose: 'Record an extra dose',

  correctSource: 'Correct the stock source',
  correctSourceHint:
    'Fix it if the wrong package was charged. The total stays the same and nothing is erased.',
  correctedFrom: 'Corrected',
  correctionReason: 'Reason (optional)',
  applyCorrection: 'Apply correction',
  correctionApplied: 'Stock source corrected',
  supersededAllocation: 'Superseded',

  addMedication: 'Add a medication',
  editMedication: 'Edit medication',
  addStock: 'Add stock',
  inventoryEmpty: 'No medications defined yet.',
  inventoryEmptyHint: 'Define a medication first, then add its physical packages.',
  packagesLabel: 'packages',
  packageOrdinal: 'Box',
  full: 'Full',
  opened: 'Opened',
  empty: 'Empty',
  disposed: 'Disposed',
  lost: 'Lost',
  archived: 'Archived',
  pinned: 'Active package',
  pin: 'Make the active package',
  unpin: 'Clear active package',
  retire: 'Retire',
  retireDisposed: 'Mark as disposed',
  retireLost: 'Mark as lost',
  onLoan: 'On loan',
  lend: 'Lend',
  returnLoan: 'Mark returned',
  owner: 'Owner',
  holder: 'Currently held by',
  unassigned: 'Unassigned',
  expiresOn: 'Expires',
  acquiredOn: 'Acquired',
  lotNumber: 'Lot number',
  storageLocation: 'Stored in',
  note: 'Note',
  showPackages: 'Show packages',
  hidePackages: 'Hide packages',
  archiveMedication: 'Archive medication',
  archiveKeeps:
    'No stock, package or history is deleted. The medication leaves the list, stops producing doses on Today, and is excluded from counting.',
  archiveStopsPlansBefore: 'Archiving pauses',
  archiveStopsPlansAfter:
    'standing plan(s) for this medication. Nothing is deleted: once you restore the medication you can pick them up where they left off.',
  archiveNoPlans: 'There is no standing plan for this medication.',
  restorePerson: 'Restore',
  archivePersonTitle: 'Archive person',
  archivePersonKeeps:
    'No history is deleted. They leave the lists and their doses stop appearing on Today.',
  archivePersonPausesPlansBefore: 'This pauses their',
  archivePersonPausesPlansAfter:
    'standing plan(s). Nothing is deleted: once you restore them you can pick the plans up again.',
  archivePersonNoPlans: 'There is no standing plan for this person.',
  archiveReversible: 'The medication itself, with its stock, can be restored from the “Archived” section below.',
  restoreMedication: 'Restore medication',

  medicationName: 'Medication name',
  strength: 'Strength',
  strengthPlaceholder: 'e.g. 500 mg',
  brand: 'Brand',
  manufacturer: 'Manufacturer',
  form: 'Form',
  unit: 'Unit',
  activeIngredients: 'Active ingredients',
  activeIngredientsHint: 'Separate with commas.',
  defaultPackageSize: 'Default package capacity',
  defaultPackageSizeHint:
    'Pre-fills new packages. Changing it never alters a package that already exists.',
  category: 'Category',
  tags: 'Tags',
  notes: 'Notes',

  cautionNotes: 'Safe-use notes',
  cautionNotesHint:
    'Write what your doctor, your pharmacist or the leaflet told you, in your own '
    + 'words. The app stores these notes and shows them where you take the medicine; '
    + 'it never assesses anything itself.',
  cautionNotesOwn: "Your household's own note",
  cautionDoNotTakeWith: 'Do not take together with',
  cautionDoNotTakeWithHint: 'Another medicine or a supplement, for example.',
  cautionFoodsToAvoid: 'Food and drink to avoid',
  cautionThingsToDo: 'Things to do',
  cautionThingsToDoHint: 'For example "take it with a full glass of water".',
  cautionThingsToAvoid: 'Things to avoid',
  cautionThingsToAvoidHint: 'For example "do not lie down for half an hour after it".',
  cautionWarning: 'Other warnings',

  tooSoon: 'Sooner than your own note allows',
  tooSoonStillRecordable: 'The app is not blocking you — record whatever actually happened.',
  lastTakenLabel: 'Last taken',
  earliestNextLabel: 'Earliest next',
  minimumGapShort: 'Minimum gap',
  minutesShort: 'min',

  fullPackageCount: 'Full packages',
  packageCapacity: 'Package capacity',
  openedPackage: 'Opened package',
  addOpenedPackage: '+ Add an opened package',
  remainingInPackage: 'Amount remaining',
  removeRow: 'Remove',
  looseAmount: 'Loose amount',
  assignTo: 'Assign to',
  stockPreview: 'Will add',
  stockPreviewPackages: 'packages',
  addStockAgainHint:
    'You can add more at any time — open this form again when a new box arrives, or when a lost one turns up.',

  addPlan: 'Add a plan',
  editPlan: 'Edit plan',
  plansEmpty: 'No plans yet.',
  plansEmptyHint: 'Define who takes which medication, and how.',
  person: 'Person',
  medication: 'Medication',
  dose: 'Dose',
  schedule: 'Schedule',
  scheduleDaily: 'Every day',
  scheduleWeekdays: 'Selected days',
  scheduleInterval: 'Every N days',
  scheduleAsNeeded: 'As needed',
  intervalDays: 'Day interval',
  exactTime: 'Time',
  whenInDay: 'When',
  useExactTime: 'At a set time',
  preferredTime: 'Preferred time',
  preferredTimeHint:
    'There is no fixed time, but you may have a preference. This is a note only: no dose is added to the calendar, and a day you do not take it is never counted as missed.',
  noTimePreference: 'No preference',
  preferably: 'preferably',
  asNeededExplainer:
    'An as-needed medication has no fixed time: no dose is added to the calendar, and a day you do not take it is never counted as missed. You take it when you need it and record it from Today. You can still note a preferred part of the day and how it relates to food.',
  mealRelation: 'Food timing',
  mealRelationHint:
    'Note what your doctor or the leaflet told you, as written. The app does not interpret it; it only shows it back to you when you take the medication.',
  minimumInterval: 'Minimum gap (minutes)',
  effectiveFrom: 'From',
  effectiveTo: 'Until',
  instructions: 'Instruction note',
  planVersionNote: 'Editing creates a new version; past records are unchanged.',
  deletePlan: 'End plan',
  pausePlan: 'Pause for now',
  resumePlan: 'Start again',
  planPaused: 'Paused',
  planMedicationArchived: 'Restore the medication from the archive before resuming.',
  planPersonArchived: 'Restore the person from the archive before resuming.',
  medicationHasPausedPlan:
    'There is a paused plan for this medication. If you have started taking it again, you can resume it here.',

  monday: 'Mon',
  tuesday: 'Tue',
  wednesday: 'Wed',
  thursday: 'Thu',
  friday: 'Fri',
  saturday: 'Sat',
  sunday: 'Sun',

  morning: 'Morning',
  noon: 'Noon',
  afternoon: 'Afternoon',
  evening: 'Evening',
  night: 'Night',
  bedtime: 'Bedtime',

  fasting: 'Fasting',
  fullStomach: 'On a full stomach',
  beforeFood: 'Before food',
  withFood: 'With food',
  afterFood: 'After food',

  addPerson: 'Add a person',
  personName: 'Name',
  peopleEmpty: 'No people added yet.',
  peopleEmptyHint: 'Add the people whose medication you are organising.',
  archivePerson: 'Archive person',
  rename: 'Rename',

  refill: 'Refill',
  refillSettings: 'Refill settings',
  lowStockThreshold: 'Low-stock threshold',
  lowStockDays: 'Low-stock warning (days)',
  nextEligibleRefill: 'Official refill date',
  nextEligibleRefillHint:
    'The earliest date the prescription may be filled again. Independent of stock.',
  lowStockWarning: 'Running low',
  depletionOn: 'Projected to run out',
  daysRemaining: 'days left',
  refillGapWarning: 'Refill gap',
  refillGapDetail: 'days at risk of having none',
  alreadyDepleted: 'Out of stock',
  notForecastable: 'A scheduled plan is needed to forecast',

  historyEmpty: 'Nothing recorded yet.',
  historyInventory: 'Stock movements',
  historyAdministrations: 'Recorded doses',
  historyCorrections: 'Corrections',
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
  lateBy: 'minutes late',
  earlyBy: 'minutes early',

  save: 'Save',
  cancel: 'Cancel',
  close: 'Close',
  optional: 'optional',
  loading: 'Loading…',
  retry: 'Try again',
  none: 'None',
  total: 'Total',
  remaining: 'Remaining',

  errorGeneric: 'Something went wrong. Please try again.',
  errorNetwork: 'Could not reach the server.',
  errorUnauthenticated: 'Your session ended. Please sign in again.',
  errorForbidden: 'You do not have access to this household.',
  errorInsufficientStock: 'There is not enough stock for this dose. Choose a source under advanced options.',
  errorChosenSourceInsufficient: 'The source you chose does not hold enough for this dose.',
  errorPackageNotEligible: 'That package cannot be used.',
  errorPackageNotFound: 'Package not found.',
  errorTargetInsufficientStock:
    'The target package does not hold enough. That is a counting problem; a correction cannot create negative stock.',
  errorTargetNotEligible: 'The target package cannot be used.',
  errorSameSource: 'That is already the source; there is nothing to correct.',
  errorAllocationNotActive: 'This record has already been corrected.',
  errorAdministrationUntracked: 'This dose did not draw on stock, so there is no source to correct.',
  errorAccountExists: 'An account already exists for this email.',
  errorPasswordMismatch: 'The passwords do not match.',
  errorInvalidCredentials: 'That email or password is not correct.',
  errorStaleRevision: 'A newer count exists. Review that one first.',
  errorPackageOnLoan: 'The package is currently on loan.',

  reports: 'Reports',
  reportsAdherence: 'Dose summary',
  reportsInventory: 'Stock summary',
  reportPeriod: 'Period',
  reportPeriodLast7: 'Last 7 days',
  reportPeriodLast30: 'Last 30 days',
  reportPeriodLast90: 'Last 90 days',
  reportPeriodThisMonth: 'This month',
  reportEmpty: 'No doses were recorded or planned in this period.',
  reportEmptyHint: 'Add a plan or record a dose and the summary will appear here.',
  reportHouseholdTotal: 'Household total',
  reportPlanned: 'Planned',
  reportTakenOnSchedule: 'Taken on schedule',
  reportMissed: 'Not recorded',
  reportSkipped: 'Skipped',
  reportPartial: 'Partial',
  reportExtra: 'Extra',
  reportRecorded: 'Recorded doses',
  reportNoScheduledDoses: 'Nothing scheduled',
  reportDescriptiveNotice:
    'These counts show only what was planned and what was recorded. They are not an assessment or a recommendation.',
  reportUnknownTimeZone:
    "A plan's time zone is not installed on this server, so its doses were not counted.",
  reportStockRemaining: 'Remaining',
  reportDepletion: 'Runs out',
  reportDaysLeft: 'days',
  reportNotForecastable: 'Cannot be forecast',
  reportLowStock: 'Low stock',
  reportRefillGap: 'Refill gap',
  reportRefillGapDays: 'days without medication',
  reportLowStockSummary: 'low on stock',
  reportRefillGapSummary: 'with a refill gap',

  export: 'Export',
  exportTitle: 'Download your data',
  exportDescription:
    "Download your household's medication history as a file: medications, packages, stock movements, plans, and every recorded dose.",
  exportButton: 'Download JSON file',
  exportExcludesNotice: 'The file contains no account details, passwords, or session data.',
  exportPreparing: 'Preparing…',
  exportDone: 'File downloaded.',

  counting: 'Count',
  countingTitle: 'Count what you have',
  countingDescription:
    'Enter the amount you counted. Only the rows you fill in are recorded; anything left blank is untouched.',
  countingExpected: 'On record',
  countingObserved: 'You counted',
  countingObservedHint: 'For example: 12, 7½, 3/2',
  countingSubmit: 'Record the count',
  countingSubmitting: 'Recording…',
  countingNote: 'Note',
  countingNoteOptional: 'optional',
  countingNothingEntered: 'Enter at least one amount to record a count.',
  countingInvalidAmount: 'That amount could not be read.',
  countingAccepted: 'Count recorded.',
  countingAdvanced: 'Advanced: count by box',
  countingByPackage: 'Count box by box',
  countingByPackageHint:
    'Enter each box separately. Only the boxes you enter are reconciled, not the medication as a whole.',
  countingWholeMedication: 'Count the whole medication',
  countingEmpty: 'There is nothing to count.',
  countingEmptyHint: 'Define a medication first.',

  countingHistory: 'Past counts',
  countingHistoryEmpty: 'No count has been recorded yet.',
  countingRevision: 'Correction',
  countingRevisionNumber: 'revision',
  countingCorrect: 'Correct',
  countingCorrecting: 'You are correcting this count',
  countingCorrectingHint:
    'The recorded count is never changed; your correction is appended to it as a new revision.',
  countingCancelCorrection: 'Cancel the correction',
  countingSuperseded: 'A newer revision exists',
  countingMatched: 'Count matched',
  countingDelta: 'Difference',

  safetyNotice:
    'This application organises and tracks medication. It does not diagnose, recommend a dose, or assess drug interactions.',
};

export const dictionaries = { tr, en } as const;

export type Locale = keyof typeof dictionaries;

export const LOCALES: readonly Locale[] = ['tr', 'en'];

export const LOCALE_STORAGE_KEY = 'medication-locale';

export type Translate = (key: MessageKey) => string;

const LocaleContext = createContext<{ locale: Locale; t: Translate; setLocale: (l: Locale) => void }>({
  locale: 'tr',
  t: (key) => tr[key],
  setLocale: () => {},
});

export const LocaleProvider = LocaleContext.Provider;

export function useLocale() {
  return useContext(LocaleContext);
}

/** Maps a server refusal code onto a translated message. */
export function errorKey(code: string): MessageKey {
  const map: Record<string, MessageKey> = {
    unauthenticated: 'errorUnauthenticated',
    forbidden: 'errorForbidden',
    insufficient_stock: 'errorInsufficientStock',
    chosen_source_insufficient: 'errorChosenSourceInsufficient',
    package_not_eligible: 'errorPackageNotEligible',
    package_not_found: 'errorPackageNotFound',
    package_not_specified: 'errorPackageNotFound',
    target_insufficient_stock: 'errorTargetInsufficientStock',
    target_not_eligible: 'errorTargetNotEligible',
    same_source: 'errorSameSource',
    allocation_not_active: 'errorAllocationNotActive',
    administration_untracked: 'errorAdministrationUntracked',
    account_exists: 'errorAccountExists',
    password_mismatch: 'errorPasswordMismatch',
    invalid_credentials: 'errorInvalidCredentials',
    stale_revision: 'errorStaleRevision',
    package_on_loan: 'errorPackageOnLoan',
  };

  return map[code] ?? 'errorGeneric';
}

/**
 * The caution notes a household actually wrote, labelled and in a fixed order.
 *
 * One list builder for every screen that shows them, so the Today row and the medicine's
 * own card cannot drift into labelling the same note two different ways. Blank notes are
 * dropped rather than rendered empty.
 *
 * The order is deliberate: what not to take it with comes first, because that is the one
 * a person needs before they swallow anything, and a general warning comes last because
 * it is the one they most often already know.
 */
export function cautionList(
  cautions: CautionNotes | null | undefined,
  t: (key: MessageKey) => string,
): { label: string; value: string }[] {
  if (!cautions) {
    return [];
  }

  const ordered: [MessageKey, string | null][] = [
    ['cautionDoNotTakeWith', cautions.doNotTakeWith],
    ['cautionFoodsToAvoid', cautions.foodsToAvoid],
    ['cautionThingsToDo', cautions.thingsToDo],
    ['cautionThingsToAvoid', cautions.thingsToAvoid],
    ['cautionWarning', cautions.warning],
  ];

  return ordered
    .filter((entry): entry is [MessageKey, string] => (entry[1] ?? '').trim() !== '')
    .map(([key, value]) => ({ label: t(key), value: value.trim() }));
}

/** Translation keys for the enum names the API returns. */
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
    Sealed: 'full',
    Opened: 'opened',
    Disposed: 'disposed',
    Lost: 'lost',
    Archived: 'archived',
    Acquire: 'entryAcquire',
    Consume: 'entryConsume',
    CorrectionReversal: 'entryCorrectionReversal',
    CorrectionConsume: 'entryCorrectionConsume',
    Found: 'entryFound',
    Loss: 'entryLoss',
    Dispose: 'entryDispose',
    CountAdjustment: 'entryCountAdjustment',
    PackageTransfer: 'entryPackageTransfer',
    ManualAdjustment: 'entryManualAdjustment',
  };

  return map[value] ?? null;
}
