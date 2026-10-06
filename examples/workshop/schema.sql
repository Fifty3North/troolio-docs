CREATE TABLE dbo.WorkshopRegistrationCard
(
    RegistrationId uniqueidentifier NOT NULL PRIMARY KEY,
    WorkshopId uniqueidentifier NOT NULL,
    AttendeeId uniqueidentifier NOT NULL,
    DisplayName nvarchar(120) NOT NULL,
    Status varchar(16) NOT NULL,
    SourceStream nvarchar(256) NOT NULL,
    SourceVersion bigint NOT NULL,
    DomainHash char(64) NOT NULL
);
CREATE INDEX IX_WorkshopRegistrationCard_Attendee
    ON dbo.WorkshopRegistrationCard (AttendeeId, WorkshopId, RegistrationId);

CREATE TABLE dbo.WorkshopProjectionInbox
(
    Consumer nvarchar(64) NOT NULL,
    SourceStream nvarchar(256) NOT NULL,
    SourceVersion bigint NOT NULL,
    DomainHash char(64) NOT NULL,
    CONSTRAINT PK_WorkshopProjectionInbox PRIMARY KEY (Consumer, SourceStream, SourceVersion)
);
