use master
go
DROP DATABASE BlogEngine
go
CREATE DATABASE BlogEngine
go
use BlogEngine
go
exec sp_addrolemember 'db_datareader', 'NT AUTHORITY\NETWORK SERVICE' 
go
exec sp_addrolemember 'db_datawriter', 'NT AUTHORITY\NETWORK SERVICE' 
go
exec sp_addrolemember 'db_owner', 'NT AUTHORITY\NETWORK SERVICE'
go