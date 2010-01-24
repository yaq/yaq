if not exists (select * from sys.schemas where name = N'Queue')
begin
	print 'Create schema'
	exec sys.sp_executesql N'CREATE SCHEMA [Queue] AUTHORIZATION [dbo]'
end

print 'Create Queue tables'

if exists (select * from sys.objects where object_id = object_id(N'[Queue].[Message]') and type in (N'U'))
drop table [Queue].[Message]

if not exists (select * from sys.objects where object_id = object_id(N'[Queue].[Message]') and type in (N'U'))
begin
	print 'Create [Message] table'
	create table [Queue].[Message] (
		Id bigint identity(1,1),
		Queue varchar(80),
		PopReceipt uniqueidentifier,
		Content varbinary(8000),
		TakenTill datetime,
		Queued datetime constraint [Message.DF.ForQueued] default (getutcdate()),
		Version TimeStamp,
		constraint [Message.PK.ById] primary key clustered (Id),
	)
	
	create nonclustered index [Message.IX.ByQueue] ON [Queue].[Message] ([Queue]) include (TakenTill)
end 