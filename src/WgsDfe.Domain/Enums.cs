namespace WgsDfe.Domain;
public enum DocumentModel { NFe=55, CTe=57, NFCom=62, NFCe=65, CTeOS=67 }
[Flags] public enum EnabledDocuments { None=0, NFe=1, NFCe=2, CTe=4, CTeOS=8, NFCom=16, All=31 }
public enum FiscalEnvironment { Production=1, Homologation=2 }
public enum DocumentStatus { Unknown, Authorized, Cancelled, Denied, Other }
public enum Availability { Pending, Available, NotFoundInSource, OutsideWindow, CertificateRequired, Unauthorized, InvalidKey, TemporaryError, NotRecovered }
public enum JobStatus { Pending, Running, Completed, CompletedWithWarnings, Failed, Cancelled }
public enum JobKind { Download, HistoricalRecovery, Import, Export }
public enum ActorFilter { Issuer=1, Recipient=2, Either=3, OtherParticipant=4, Both=9 }
public enum PayloadMode { KeysOnly=0, FullXml=1 }
public enum CertificateRequirement { None, Participant, AccountantWithLink }
public enum AuditAction { FoundationInitialized, CompanyCreated, CompanyUpdated, CertificateReplaced, DownloadStarted, XmlImported, XmlRecovered, ExportCompleted, CompanyActivated, CompanyInactivated, CompanyDeleted, CompanyImportStarted, CompanyImportCompleted, CompanyImportCancelled, CertificateAdded, CertificateRemoved, CertificateTested, CertificateAssociated, CertificateUnlinked, ArchiveImportStarted, ArchiveImportCompleted, ArchiveImportCancelled, ArchiveImportFailed, ArchiveVariantStored, ArchiveLinksUpdated, NfeSyncCompleted, NfePageCommitted, NfeRequestStarted, CtePageCommitted, CteSyncCompleted, CteRequestStarted, HistoricalCallReserved, HistoricalRecoveryCompleted, ExportStarted, ExportPrepared, ExportFailed, ExportCancelled, ScheduleSaved, SchedulePaused, ScheduleDeleted, ScheduleRunStarted, ScheduleRunCompleted, ScheduleRunInterrupted, ArchiveLocalCopyRemoved }

public enum CertificateUse { Participant, Accountant }
