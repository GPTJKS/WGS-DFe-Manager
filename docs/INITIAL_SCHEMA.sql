CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
    "ProductVersion" TEXT NOT NULL
);

BEGIN TRANSACTION;
CREATE TABLE "Organizations" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Organizations" PRIMARY KEY,
    "Name" TEXT NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL
);

CREATE TABLE "ApplicationSettings" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_ApplicationSettings" PRIMARY KEY,
    "Key" TEXT NOT NULL,
    "Value" TEXT NOT NULL,
    "OrganizationId" TEXT NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NOT NULL,
    "Revision" INTEGER NOT NULL,
    CONSTRAINT "AK_ApplicationSettings_OrganizationId_Id" UNIQUE ("OrganizationId", "Id"),
    CONSTRAINT "FK_ApplicationSettings_Organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES "Organizations" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "AuditLogs" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AuditLogs" PRIMARY KEY,
    "Action" INTEGER NOT NULL,
    "EntityId" TEXT NULL,
    "CorrelationId" TEXT NOT NULL,
    "ResultCode" TEXT NOT NULL,
    "OrganizationId" TEXT NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NOT NULL,
    "Revision" INTEGER NOT NULL,
    CONSTRAINT "AK_AuditLogs_OrganizationId_Id" UNIQUE ("OrganizationId", "Id"),
    CONSTRAINT "FK_AuditLogs_Organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES "Organizations" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "Certificates" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Certificates" PRIMARY KEY,
    "Label" TEXT NOT NULL,
    "Subject" TEXT NOT NULL,
    "HolderTaxId" TEXT NULL,
    "Thumbprint" TEXT NOT NULL,
    "ValidFromUtc" TEXT NOT NULL,
    "ValidToUtc" TEXT NOT NULL,
    "SecureStoreReference" TEXT NOT NULL,
    "OrganizationId" TEXT NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NOT NULL,
    "Revision" INTEGER NOT NULL,
    CONSTRAINT "AK_Certificates_OrganizationId_Id" UNIQUE ("OrganizationId", "Id"),
    CONSTRAINT "FK_Certificates_Organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES "Organizations" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "Companies" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Companies" PRIMARY KEY,
    "LegalName" TEXT NOT NULL,
    "TradeName" TEXT NULL,
    "Cnpj" TEXT NOT NULL,
    "StateRegistration" TEXT NULL,
    "State" TEXT NOT NULL,
    "City" TEXT NULL,
    "IsActive" INTEGER NOT NULL,
    "EnabledDocuments" INTEGER NOT NULL,
    "StorageRelativePath" TEXT NULL,
    "Notes" TEXT NULL,
    "LastSynchronizationAtUtc" TEXT NULL,
    "ConnectionStatusCode" TEXT NULL,
    "AccountantLinkStatusCode" TEXT NULL,
    "OrganizationId" TEXT NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NOT NULL,
    "Revision" INTEGER NOT NULL,
    CONSTRAINT "AK_Companies_OrganizationId_Id" UNIQUE ("OrganizationId", "Id"),
    CONSTRAINT "CK_Company_CnpjLength" CHECK (length(Cnpj) = 14),
    CONSTRAINT "FK_Companies_Organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES "Organizations" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "DocumentProviders" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_DocumentProviders" PRIMARY KEY,
    "Code" TEXT NOT NULL,
    "DisplayName" TEXT NOT NULL,
    "IsEnabled" INTEGER NOT NULL,
    "Priority" INTEGER NOT NULL,
    "CertificateRequirement" INTEGER NOT NULL,
    "OrganizationId" TEXT NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NOT NULL,
    "Revision" INTEGER NOT NULL,
    CONSTRAINT "AK_DocumentProviders_OrganizationId_Id" UNIQUE ("OrganizationId", "Id"),
    CONSTRAINT "FK_DocumentProviders_Organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES "Organizations" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "FiscalDocuments" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_FiscalDocuments" PRIMARY KEY,
    "AccessKey" TEXT NOT NULL,
    "Model" INTEGER NOT NULL,
    "Environment" INTEGER NOT NULL,
    "IssuerTaxId" TEXT NULL,
    "RecipientTaxId" TEXT NULL,
    "IssuerName" TEXT NULL,
    "RecipientName" TEXT NULL,
    "IssueDateUtc" TEXT NULL,
    "ReferenceYear" INTEGER NULL,
    "ReferenceMonth" INTEGER NULL,
    "Series" TEXT NULL,
    "DocumentNumber" TEXT NULL,
    "TotalValue" TEXT NULL,
    "Status" INTEGER NOT NULL,
    "ProtocolNumber" TEXT NULL,
    "AuthorizationDateUtc" TEXT NULL,
    "CancellationDateUtc" TEXT NULL,
    "FileRelativePath" TEXT NOT NULL,
    "XmlHash" TEXT NOT NULL,
    "SourceProviderCode" TEXT NOT NULL,
    "RetrievedAtUtc" TEXT NOT NULL,
    "OrganizationId" TEXT NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NOT NULL,
    "Revision" INTEGER NOT NULL,
    CONSTRAINT "AK_FiscalDocuments_OrganizationId_Id" UNIQUE ("OrganizationId", "Id"),
    CONSTRAINT "CK_Document_KeyLength" CHECK (length(AccessKey) = 44),
    CONSTRAINT "FK_FiscalDocuments_Organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES "Organizations" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "ProcessingJobs" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_ProcessingJobs" PRIMARY KEY,
    "Kind" INTEGER NOT NULL,
    "Status" INTEGER NOT NULL,
    "StartedAtUtc" TEXT NULL,
    "CompletedAtUtc" TEXT NULL,
    "TotalItems" INTEGER NOT NULL,
    "ProcessedItems" INTEGER NOT NULL,
    "LastErrorCode" TEXT NULL,
    "OrganizationId" TEXT NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NOT NULL,
    "Revision" INTEGER NOT NULL,
    CONSTRAINT "AK_ProcessingJobs_OrganizationId_Id" UNIQUE ("OrganizationId", "Id"),
    CONSTRAINT "FK_ProcessingJobs_Organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES "Organizations" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "CertificateAssociations" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_CertificateAssociations" PRIMARY KEY,
    "CompanyId" TEXT NOT NULL,
    "CertificateId" TEXT NOT NULL,
    "ProviderCode" TEXT NOT NULL,
    "OrganizationId" TEXT NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NOT NULL,
    "Revision" INTEGER NOT NULL,
    CONSTRAINT "AK_CertificateAssociations_OrganizationId_Id" UNIQUE ("OrganizationId", "Id"),
    CONSTRAINT "FK_CertificateAssociations_Certificates_OrganizationId_CertificateId" FOREIGN KEY ("OrganizationId", "CertificateId") REFERENCES "Certificates" ("OrganizationId", "Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_CertificateAssociations_Companies_OrganizationId_CompanyId" FOREIGN KEY ("OrganizationId", "CompanyId") REFERENCES "Companies" ("OrganizationId", "Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_CertificateAssociations_Organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES "Organizations" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "DocumentSyncStates" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_DocumentSyncStates" PRIMARY KEY,
    "CompanyId" TEXT NOT NULL,
    "ProviderCode" TEXT NOT NULL,
    "Model" INTEGER NOT NULL,
    "Environment" INTEGER NOT NULL,
    "Actor" INTEGER NOT NULL,
    "PayloadMode" INTEGER NOT NULL,
    "LastNsu" TEXT NOT NULL,
    "LastSuccessAtUtc" TEXT NULL,
    "NextAllowedAtUtc" TEXT NULL,
    "LastResponseCode" INTEGER NULL,
    "OrganizationId" TEXT NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NOT NULL,
    "Revision" INTEGER NOT NULL,
    CONSTRAINT "AK_DocumentSyncStates_OrganizationId_Id" UNIQUE ("OrganizationId", "Id"),
    CONSTRAINT "FK_DocumentSyncStates_Companies_OrganizationId_CompanyId" FOREIGN KEY ("OrganizationId", "CompanyId") REFERENCES "Companies" ("OrganizationId", "Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_DocumentSyncStates_Organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES "Organizations" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "KnownDocumentKeys" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_KnownDocumentKeys" PRIMARY KEY,
    "CompanyId" TEXT NOT NULL,
    "AccessKey" TEXT NOT NULL,
    "Environment" INTEGER NOT NULL,
    "Availability" INTEGER NOT NULL,
    "Source" TEXT NOT NULL,
    "OrganizationId" TEXT NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NOT NULL,
    "Revision" INTEGER NOT NULL,
    CONSTRAINT "AK_KnownDocumentKeys_OrganizationId_Id" UNIQUE ("OrganizationId", "Id"),
    CONSTRAINT "FK_KnownDocumentKeys_Companies_OrganizationId_CompanyId" FOREIGN KEY ("OrganizationId", "CompanyId") REFERENCES "Companies" ("OrganizationId", "Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_KnownDocumentKeys_Organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES "Organizations" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "FiscalDocumentCompanies" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_FiscalDocumentCompanies" PRIMARY KEY,
    "CompanyId" TEXT NOT NULL,
    "FiscalDocumentId" TEXT NOT NULL,
    "Role" INTEGER NOT NULL,
    "OrganizationId" TEXT NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NOT NULL,
    "Revision" INTEGER NOT NULL,
    CONSTRAINT "AK_FiscalDocumentCompanies_OrganizationId_Id" UNIQUE ("OrganizationId", "Id"),
    CONSTRAINT "FK_FiscalDocumentCompanies_Companies_OrganizationId_CompanyId" FOREIGN KEY ("OrganizationId", "CompanyId") REFERENCES "Companies" ("OrganizationId", "Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_FiscalDocumentCompanies_FiscalDocuments_OrganizationId_FiscalDocumentId" FOREIGN KEY ("OrganizationId", "FiscalDocumentId") REFERENCES "FiscalDocuments" ("OrganizationId", "Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_FiscalDocumentCompanies_Organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES "Organizations" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "FiscalDocumentEvents" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_FiscalDocumentEvents" PRIMARY KEY,
    "FiscalDocumentId" TEXT NULL,
    "AccessKey" TEXT NOT NULL,
    "Environment" INTEGER NOT NULL,
    "EventTypeCode" TEXT NOT NULL,
    "SequenceNumber" INTEGER NOT NULL,
    "ProtocolNumber" TEXT NULL,
    "EventDateUtc" TEXT NULL,
    "FileRelativePath" TEXT NOT NULL,
    "XmlHash" TEXT NOT NULL,
    "SourceProviderCode" TEXT NOT NULL,
    "OrganizationId" TEXT NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NOT NULL,
    "Revision" INTEGER NOT NULL,
    CONSTRAINT "AK_FiscalDocumentEvents_OrganizationId_Id" UNIQUE ("OrganizationId", "Id"),
    CONSTRAINT "FK_FiscalDocumentEvents_FiscalDocuments_OrganizationId_FiscalDocumentId" FOREIGN KEY ("OrganizationId", "FiscalDocumentId") REFERENCES "FiscalDocuments" ("OrganizationId", "Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_FiscalDocumentEvents_Organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES "Organizations" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "ProcessingJobItems" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_ProcessingJobItems" PRIMARY KEY,
    "JobId" TEXT NOT NULL,
    "CompanyId" TEXT NULL,
    "ItemReference" TEXT NOT NULL,
    "Model" INTEGER NULL,
    "Availability" INTEGER NOT NULL,
    "Status" INTEGER NOT NULL,
    "ProviderCode" TEXT NULL,
    "ErrorCode" TEXT NULL,
    "AttemptCount" INTEGER NOT NULL,
    "OrganizationId" TEXT NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NOT NULL,
    "Revision" INTEGER NOT NULL,
    CONSTRAINT "AK_ProcessingJobItems_OrganizationId_Id" UNIQUE ("OrganizationId", "Id"),
    CONSTRAINT "FK_ProcessingJobItems_Companies_OrganizationId_CompanyId" FOREIGN KEY ("OrganizationId", "CompanyId") REFERENCES "Companies" ("OrganizationId", "Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_ProcessingJobItems_Organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES "Organizations" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_ProcessingJobItems_ProcessingJobs_OrganizationId_JobId" FOREIGN KEY ("OrganizationId", "JobId") REFERENCES "ProcessingJobs" ("OrganizationId", "Id") ON DELETE RESTRICT
);

CREATE UNIQUE INDEX "IX_ApplicationSettings_OrganizationId_Key" ON "ApplicationSettings" ("OrganizationId", "Key");

CREATE INDEX "IX_AuditLogs_OrganizationId_CreatedAtUtc" ON "AuditLogs" ("OrganizationId", "CreatedAtUtc");

CREATE INDEX "IX_CertificateAssociations_OrganizationId_CertificateId" ON "CertificateAssociations" ("OrganizationId", "CertificateId");

CREATE UNIQUE INDEX "IX_CertificateAssociations_OrganizationId_CompanyId_ProviderCode" ON "CertificateAssociations" ("OrganizationId", "CompanyId", "ProviderCode");

CREATE UNIQUE INDEX "IX_Companies_OrganizationId_Cnpj" ON "Companies" ("OrganizationId", "Cnpj");

CREATE UNIQUE INDEX "IX_DocumentProviders_OrganizationId_Code" ON "DocumentProviders" ("OrganizationId", "Code");

CREATE UNIQUE INDEX "IX_DocumentSyncStates_OrganizationId_CompanyId_ProviderCode_Model_Environment_Actor_PayloadMode" ON "DocumentSyncStates" ("OrganizationId", "CompanyId", "ProviderCode", "Model", "Environment", "Actor", "PayloadMode");

CREATE UNIQUE INDEX "IX_FiscalDocumentCompanies_OrganizationId_CompanyId_FiscalDocumentId" ON "FiscalDocumentCompanies" ("OrganizationId", "CompanyId", "FiscalDocumentId");

CREATE INDEX "IX_FiscalDocumentCompanies_OrganizationId_FiscalDocumentId" ON "FiscalDocumentCompanies" ("OrganizationId", "FiscalDocumentId");

CREATE UNIQUE INDEX "IX_FiscalDocumentEvents_OrganizationId_Environment_AccessKey_EventTypeCode_SequenceNumber" ON "FiscalDocumentEvents" ("OrganizationId", "Environment", "AccessKey", "EventTypeCode", "SequenceNumber");

CREATE INDEX "IX_FiscalDocumentEvents_OrganizationId_FiscalDocumentId" ON "FiscalDocumentEvents" ("OrganizationId", "FiscalDocumentId");

CREATE UNIQUE INDEX "IX_FiscalDocuments_OrganizationId_Environment_AccessKey" ON "FiscalDocuments" ("OrganizationId", "Environment", "AccessKey");

CREATE INDEX "IX_FiscalDocuments_OrganizationId_IssueDateUtc_Model" ON "FiscalDocuments" ("OrganizationId", "IssueDateUtc", "Model");

CREATE UNIQUE INDEX "IX_KnownDocumentKeys_OrganizationId_CompanyId_Environment_AccessKey" ON "KnownDocumentKeys" ("OrganizationId", "CompanyId", "Environment", "AccessKey");

CREATE INDEX "IX_ProcessingJobItems_OrganizationId_CompanyId" ON "ProcessingJobItems" ("OrganizationId", "CompanyId");

CREATE UNIQUE INDEX "IX_ProcessingJobItems_OrganizationId_JobId_ItemReference" ON "ProcessingJobItems" ("OrganizationId", "JobId", "ItemReference");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261007200457_InitialFoundation', '10.0.12');

COMMIT;

BEGIN TRANSACTION;
ALTER TABLE "Companies" ADD "SearchText" TEXT NOT NULL DEFAULT '';

UPDATE Companies SET SearchText = LegalName || ' ' || COALESCE(TradeName, '') || ' ' || Cnpj || ' ' || substr(Cnpj,1,2) || '.' || substr(Cnpj,3,3) || '.' || substr(Cnpj,6,3) || '/' || substr(Cnpj,9,4) || '-' || substr(Cnpj,13,2) || ' ' || COALESCE(City, '');

UPDATE Companies SET SearchText = replace(SearchText, 'á', 'a');

UPDATE Companies SET SearchText = replace(SearchText, 'à', 'a');

UPDATE Companies SET SearchText = replace(SearchText, 'â', 'a');

UPDATE Companies SET SearchText = replace(SearchText, 'ã', 'a');

UPDATE Companies SET SearchText = replace(SearchText, 'ä', 'a');

UPDATE Companies SET SearchText = replace(SearchText, 'é', 'e');

UPDATE Companies SET SearchText = replace(SearchText, 'è', 'e');

UPDATE Companies SET SearchText = replace(SearchText, 'ê', 'e');

UPDATE Companies SET SearchText = replace(SearchText, 'ë', 'e');

UPDATE Companies SET SearchText = replace(SearchText, 'í', 'i');

UPDATE Companies SET SearchText = replace(SearchText, 'ì', 'i');

UPDATE Companies SET SearchText = replace(SearchText, 'î', 'i');

UPDATE Companies SET SearchText = replace(SearchText, 'ï', 'i');

UPDATE Companies SET SearchText = replace(SearchText, 'ó', 'o');

UPDATE Companies SET SearchText = replace(SearchText, 'ò', 'o');

UPDATE Companies SET SearchText = replace(SearchText, 'ô', 'o');

UPDATE Companies SET SearchText = replace(SearchText, 'õ', 'o');

UPDATE Companies SET SearchText = replace(SearchText, 'ö', 'o');

UPDATE Companies SET SearchText = replace(SearchText, 'ú', 'u');

UPDATE Companies SET SearchText = replace(SearchText, 'ù', 'u');

UPDATE Companies SET SearchText = replace(SearchText, 'û', 'u');

UPDATE Companies SET SearchText = replace(SearchText, 'ü', 'u');

UPDATE Companies SET SearchText = replace(SearchText, 'ç', 'c');

UPDATE Companies SET SearchText = replace(SearchText, 'Á', 'A');

UPDATE Companies SET SearchText = replace(SearchText, 'À', 'A');

UPDATE Companies SET SearchText = replace(SearchText, 'Â', 'A');

UPDATE Companies SET SearchText = replace(SearchText, 'Ã', 'A');

UPDATE Companies SET SearchText = replace(SearchText, 'Ä', 'A');

UPDATE Companies SET SearchText = replace(SearchText, 'É', 'E');

UPDATE Companies SET SearchText = replace(SearchText, 'È', 'E');

UPDATE Companies SET SearchText = replace(SearchText, 'Ê', 'E');

UPDATE Companies SET SearchText = replace(SearchText, 'Ë', 'E');

UPDATE Companies SET SearchText = replace(SearchText, 'Í', 'I');

UPDATE Companies SET SearchText = replace(SearchText, 'Ì', 'I');

UPDATE Companies SET SearchText = replace(SearchText, 'Î', 'I');

UPDATE Companies SET SearchText = replace(SearchText, 'Ï', 'I');

UPDATE Companies SET SearchText = replace(SearchText, 'Ó', 'O');

UPDATE Companies SET SearchText = replace(SearchText, 'Ò', 'O');

UPDATE Companies SET SearchText = replace(SearchText, 'Ô', 'O');

UPDATE Companies SET SearchText = replace(SearchText, 'Õ', 'O');

UPDATE Companies SET SearchText = replace(SearchText, 'Ö', 'O');

UPDATE Companies SET SearchText = replace(SearchText, 'Ú', 'U');

UPDATE Companies SET SearchText = replace(SearchText, 'Ù', 'U');

UPDATE Companies SET SearchText = replace(SearchText, 'Û', 'U');

UPDATE Companies SET SearchText = replace(SearchText, 'Ü', 'U');

UPDATE Companies SET SearchText = replace(SearchText, 'Ç', 'C');

UPDATE Companies SET SearchText = trim(upper(SearchText));

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261007203626_CompanySearchIndex', '10.0.12');

COMMIT;

BEGIN TRANSACTION;
ALTER TABLE "Certificates" ADD "Use" INTEGER NOT NULL DEFAULT 0;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261007234620_CertificatePurpose', '10.0.12');

COMMIT;

BEGIN TRANSACTION;
ALTER TABLE "FiscalDocuments" ADD "DeclaredStatus" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "FiscalDocuments" ADD "IssueDateOriginal" TEXT NULL;

ALTER TABLE "FiscalDocuments" ADD "IssueLocalDate" TEXT NULL;

ALTER TABLE "FiscalDocuments" ADD "ResponseCode" INTEGER NULL;

ALTER TABLE "FiscalDocuments" ADD "SearchText" TEXT NOT NULL DEFAULT '';

ALTER TABLE "FiscalDocuments" ADD "TotalCents" INTEGER NULL;

ALTER TABLE "FiscalDocumentEvents" ADD "EventDateOriginal" TEXT NULL;

ALTER TABLE "FiscalDocumentEvents" ADD "Model" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "FiscalDocumentEvents" ADD "ReceiptDateUtc" TEXT NULL;

ALTER TABLE "FiscalDocumentEvents" ADD "ResponseCode" INTEGER NULL;

CREATE TABLE "ArchiveFiles" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_ArchiveFiles" PRIMARY KEY,
    "FiscalDocumentId" TEXT NULL,
    "FiscalDocumentEventId" TEXT NULL,
    "Hash" TEXT NOT NULL,
    "RelativePath" TEXT NOT NULL,
    "ByteLength" INTEGER NOT NULL,
    "IsConflict" INTEGER NOT NULL,
    "ResponseCode" INTEGER NULL,
    "OrganizationId" TEXT NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NOT NULL,
    "Revision" INTEGER NOT NULL,
    CONSTRAINT "AK_ArchiveFiles_OrganizationId_Id" UNIQUE ("OrganizationId", "Id"),
    CONSTRAINT "CK_ArchiveFile_Owner" CHECK ((FiscalDocumentId IS NOT NULL AND FiscalDocumentEventId IS NULL) OR (FiscalDocumentId IS NULL AND FiscalDocumentEventId IS NOT NULL)),
    CONSTRAINT "FK_ArchiveFiles_FiscalDocumentEvents_OrganizationId_FiscalDocumentEventId" FOREIGN KEY ("OrganizationId", "FiscalDocumentEventId") REFERENCES "FiscalDocumentEvents" ("OrganizationId", "Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_ArchiveFiles_FiscalDocuments_OrganizationId_FiscalDocumentId" FOREIGN KEY ("OrganizationId", "FiscalDocumentId") REFERENCES "FiscalDocuments" ("OrganizationId", "Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_ArchiveFiles_Organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES "Organizations" ("Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_FiscalDocuments_OrganizationId_IssueLocalDate_TotalCents" ON "FiscalDocuments" ("OrganizationId", "IssueLocalDate", "TotalCents");

CREATE INDEX "IX_ArchiveFiles_OrganizationId_FiscalDocumentEventId" ON "ArchiveFiles" ("OrganizationId", "FiscalDocumentEventId");

CREATE INDEX "IX_ArchiveFiles_OrganizationId_FiscalDocumentId" ON "ArchiveFiles" ("OrganizationId", "FiscalDocumentId");

CREATE UNIQUE INDEX "IX_ArchiveFiles_OrganizationId_Hash" ON "ArchiveFiles" ("OrganizationId", "Hash");

UPDATE FiscalDocuments SET DeclaredStatus = Status;

UPDATE FiscalDocumentEvents SET Model = CAST(substr(AccessKey,21,2) AS INTEGER) WHERE substr(AccessKey,21,2) IN ('55','57','62','65','67');

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261016183829_ArchiveFilesAndSearch', '10.0.12');

COMMIT;

BEGIN TRANSACTION;
CREATE TABLE "NfeDistributionReceipts" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_NfeDistributionReceipts" PRIMARY KEY,
    "CompanyId" TEXT NOT NULL,
    "ProviderCode" TEXT NOT NULL,
    "Environment" INTEGER NOT NULL,
    "Actor" INTEGER NOT NULL,
    "PayloadMode" INTEGER NOT NULL,
    "AccessKey" TEXT NULL,
    "RequestedNsu" TEXT NOT NULL,
    "ResponseBytes" BLOB NOT NULL,
    "ResponseHash" TEXT NOT NULL,
    "Applied" INTEGER NOT NULL,
    "ResponseCode" INTEGER NULL,
    "ResultCode" TEXT NULL,
    "OrganizationId" TEXT NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NOT NULL,
    "Revision" INTEGER NOT NULL,
    CONSTRAINT "AK_NfeDistributionReceipts_OrganizationId_Id" UNIQUE ("OrganizationId", "Id"),
    CONSTRAINT "FK_NfeDistributionReceipts_Companies_OrganizationId_CompanyId" FOREIGN KEY ("OrganizationId", "CompanyId") REFERENCES "Companies" ("OrganizationId", "Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_NfeDistributionReceipts_Organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES "Organizations" ("Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_NfeDistributionReceipts_OrganizationId_CompanyId_ProviderCode_Applied" ON "NfeDistributionReceipts" ("OrganizationId", "CompanyId", "ProviderCode", "Applied");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261016231335_NfeDistributionInbox', '10.0.12');

COMMIT;

BEGIN TRANSACTION;
ALTER TABLE "FiscalDocumentCompanies" ADD "Participation" TEXT NOT NULL DEFAULT '';

CREATE TABLE "CteDistributionReceipts" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_CteDistributionReceipts" PRIMARY KEY,
    "CompanyId" TEXT NOT NULL,
    "Environment" INTEGER NOT NULL,
    "RequestedNsu" TEXT NOT NULL,
    "SpecificNsu" TEXT NULL,
    "ResponseBytes" BLOB NOT NULL,
    "ResponseHash" TEXT NOT NULL,
    "Applied" INTEGER NOT NULL,
    "ResponseCode" INTEGER NULL,
    "OrganizationId" TEXT NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NOT NULL,
    "Revision" INTEGER NOT NULL,
    CONSTRAINT "AK_CteDistributionReceipts_OrganizationId_Id" UNIQUE ("OrganizationId", "Id"),
    CONSTRAINT "FK_CteDistributionReceipts_Companies_OrganizationId_CompanyId" FOREIGN KEY ("OrganizationId", "CompanyId") REFERENCES "Companies" ("OrganizationId", "Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_CteDistributionReceipts_Organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES "Organizations" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "CteDistributionItems" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_CteDistributionItems" PRIMARY KEY,
    "ReceiptId" TEXT NOT NULL,
    "CompanyId" TEXT NOT NULL,
    "Environment" INTEGER NOT NULL,
    "Nsu" TEXT NOT NULL,
    "Schema" TEXT NOT NULL,
    "AccessKey" TEXT NULL,
    "Model" INTEGER NULL,
    "XmlBytes" BLOB NOT NULL,
    "XmlHash" TEXT NOT NULL,
    "Archived" INTEGER NOT NULL,
    "ResultCode" TEXT NOT NULL,
    "OrganizationId" TEXT NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NOT NULL,
    "Revision" INTEGER NOT NULL,
    CONSTRAINT "AK_CteDistributionItems_OrganizationId_Id" UNIQUE ("OrganizationId", "Id"),
    CONSTRAINT "FK_CteDistributionItems_Companies_OrganizationId_CompanyId" FOREIGN KEY ("OrganizationId", "CompanyId") REFERENCES "Companies" ("OrganizationId", "Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_CteDistributionItems_CteDistributionReceipts_OrganizationId_ReceiptId" FOREIGN KEY ("OrganizationId", "ReceiptId") REFERENCES "CteDistributionReceipts" ("OrganizationId", "Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_CteDistributionItems_Organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES "Organizations" ("Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_CteDistributionItems_OrganizationId_CompanyId_Environment_Archived" ON "CteDistributionItems" ("OrganizationId", "CompanyId", "Environment", "Archived");

CREATE UNIQUE INDEX "IX_CteDistributionItems_OrganizationId_ReceiptId_Nsu" ON "CteDistributionItems" ("OrganizationId", "ReceiptId", "Nsu");

CREATE INDEX "IX_CteDistributionReceipts_OrganizationId_CompanyId_Environment_Applied" ON "CteDistributionReceipts" ("OrganizationId", "CompanyId", "Environment", "Applied");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261016232805_CteDistributionInbox', '10.0.12');

COMMIT;

BEGIN TRANSACTION;
CREATE TABLE "HistoricalRecoveryBatches" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_HistoricalRecoveryBatches" PRIMARY KEY,
    "JobId" TEXT NOT NULL,
    "FileName" TEXT NOT NULL,
    "Environment" INTEGER NOT NULL,
    "PaidCallsReserved" INTEGER NOT NULL,
    "OrganizationId" TEXT NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NOT NULL,
    "Revision" INTEGER NOT NULL,
    CONSTRAINT "AK_HistoricalRecoveryBatches_OrganizationId_Id" UNIQUE ("OrganizationId", "Id"),
    CONSTRAINT "FK_HistoricalRecoveryBatches_Organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES "Organizations" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_HistoricalRecoveryBatches_ProcessingJobs_OrganizationId_JobId" FOREIGN KEY ("OrganizationId", "JobId") REFERENCES "ProcessingJobs" ("OrganizationId", "Id") ON DELETE RESTRICT
);

CREATE TABLE "HistoricalRecoveryItems" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_HistoricalRecoveryItems" PRIMARY KEY,
    "BatchId" TEXT NOT NULL,
    "CompanyId" TEXT NULL,
    "SourceLine" INTEGER NOT NULL,
    "Input" TEXT NOT NULL,
    "AccessKey" TEXT NULL,
    "Model" INTEGER NULL,
    "IssuerTaxId" TEXT NULL,
    "Year" INTEGER NULL,
    "Month" INTEGER NULL,
    "State" INTEGER NOT NULL,
    "Availability" INTEGER NOT NULL,
    "ResultCode" TEXT NOT NULL,
    "ProviderCode" TEXT NULL,
    "ExternalJobId" TEXT NULL,
    "Attempts" INTEGER NOT NULL,
    "NextAllowedAtUtc" TEXT NULL,
    "ResponseBytes" BLOB NULL,
    "ResponseHash" TEXT NULL,
    "OrganizationId" TEXT NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NOT NULL,
    "Revision" INTEGER NOT NULL,
    CONSTRAINT "AK_HistoricalRecoveryItems_OrganizationId_Id" UNIQUE ("OrganizationId", "Id"),
    CONSTRAINT "CK_HistoricalItem_KeyLength" CHECK (AccessKey IS NULL OR length(AccessKey) = 44),
    CONSTRAINT "FK_HistoricalRecoveryItems_Companies_OrganizationId_CompanyId" FOREIGN KEY ("OrganizationId", "CompanyId") REFERENCES "Companies" ("OrganizationId", "Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_HistoricalRecoveryItems_HistoricalRecoveryBatches_OrganizationId_BatchId" FOREIGN KEY ("OrganizationId", "BatchId") REFERENCES "HistoricalRecoveryBatches" ("OrganizationId", "Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_HistoricalRecoveryItems_Organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES "Organizations" ("Id") ON DELETE RESTRICT
);

CREATE UNIQUE INDEX "IX_HistoricalRecoveryBatches_OrganizationId_JobId" ON "HistoricalRecoveryBatches" ("OrganizationId", "JobId");

CREATE INDEX "IX_HistoricalRecoveryItems_OrganizationId_BatchId_AccessKey" ON "HistoricalRecoveryItems" ("OrganizationId", "BatchId", "AccessKey");

CREATE UNIQUE INDEX "IX_HistoricalRecoveryItems_OrganizationId_BatchId_SourceLine" ON "HistoricalRecoveryItems" ("OrganizationId", "BatchId", "SourceLine");

CREATE INDEX "IX_HistoricalRecoveryItems_OrganizationId_CompanyId" ON "HistoricalRecoveryItems" ("OrganizationId", "CompanyId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261018165457_HistoricalRecovery', '10.0.12');

COMMIT;

BEGIN TRANSACTION;
CREATE TABLE "SyncSchedules" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_SyncSchedules" PRIMARY KEY,
    "CompanyId" TEXT NOT NULL,
    "ProviderCode" TEXT NOT NULL,
    "Environment" INTEGER NOT NULL,
    "Hour" INTEGER NOT NULL,
    "Minute" INTEGER NOT NULL,
    "TimeZoneId" TEXT NOT NULL,
    "Actor" INTEGER NOT NULL,
    "PayloadMode" INTEGER NOT NULL,
    "Models" INTEGER NOT NULL,
    "MaxPages" INTEGER NOT NULL,
    "Enabled" INTEGER NOT NULL,
    "NextRunAtUtc" TEXT NOT NULL,
    "LastStartedAtUtc" TEXT NULL,
    "LastCode" TEXT NULL,
    "Running" INTEGER NOT NULL,
    "OrganizationId" TEXT NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NOT NULL,
    "Revision" INTEGER NOT NULL,
    CONSTRAINT "AK_SyncSchedules_OrganizationId_Id" UNIQUE ("OrganizationId", "Id"),
    CONSTRAINT "CK_Schedule_Time" CHECK (Hour >= 0 AND Hour <= 23 AND Minute >= 0 AND Minute <= 59 AND MaxPages >= 1 AND MaxPages <= 100),
    CONSTRAINT "FK_SyncSchedules_Companies_OrganizationId_CompanyId" FOREIGN KEY ("OrganizationId", "CompanyId") REFERENCES "Companies" ("OrganizationId", "Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_SyncSchedules_Organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES "Organizations" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "SyncScheduleRuns" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_SyncScheduleRuns" PRIMARY KEY,
    "ScheduleId" TEXT NOT NULL,
    "StartedAtUtc" TEXT NOT NULL,
    "CompletedAtUtc" TEXT NULL,
    "Code" TEXT NOT NULL,
    "JobId" TEXT NULL,
    "OrganizationId" TEXT NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NOT NULL,
    "Revision" INTEGER NOT NULL,
    CONSTRAINT "AK_SyncScheduleRuns_OrganizationId_Id" UNIQUE ("OrganizationId", "Id"),
    CONSTRAINT "FK_SyncScheduleRuns_Organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES "Organizations" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_SyncScheduleRuns_ProcessingJobs_OrganizationId_JobId" FOREIGN KEY ("OrganizationId", "JobId") REFERENCES "ProcessingJobs" ("OrganizationId", "Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_SyncScheduleRuns_SyncSchedules_OrganizationId_ScheduleId" FOREIGN KEY ("OrganizationId", "ScheduleId") REFERENCES "SyncSchedules" ("OrganizationId", "Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_SyncScheduleRuns_OrganizationId_JobId" ON "SyncScheduleRuns" ("OrganizationId", "JobId");

CREATE INDEX "IX_SyncScheduleRuns_OrganizationId_ScheduleId" ON "SyncScheduleRuns" ("OrganizationId", "ScheduleId");

CREATE INDEX "IX_SyncScheduleRuns_OrganizationId_StartedAtUtc" ON "SyncScheduleRuns" ("OrganizationId", "StartedAtUtc");

CREATE UNIQUE INDEX "IX_SyncSchedules_OrganizationId_CompanyId_ProviderCode_Environment_Hour_Minute_TimeZoneId" ON "SyncSchedules" ("OrganizationId", "CompanyId", "ProviderCode", "Environment", "Hour", "Minute", "TimeZoneId");

CREATE INDEX "IX_SyncSchedules_OrganizationId_Enabled_NextRunAtUtc" ON "SyncSchedules" ("OrganizationId", "Enabled", "NextRunAtUtc");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261018173442_AutomaticSynchronization', '10.0.12');

COMMIT;

