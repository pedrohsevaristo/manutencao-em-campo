-- Run explicitly in the application database after migrations.
-- Create the login/user separately and add only that identity to this role.
IF DATABASE_PRINCIPAL_ID(N'manutencao_runtime') IS NULL
    CREATE ROLE [manutencao_runtime] AUTHORIZATION [dbo];
GO
GRANT SELECT, UPDATE ON OBJECT::[dbo].[AspNetUsers] TO [manutencao_runtime];
GRANT SELECT ON OBJECT::[dbo].[AspNetRoles] TO [manutencao_runtime];
GRANT SELECT ON OBJECT::[dbo].[AspNetUserRoles] TO [manutencao_runtime];
GRANT SELECT ON OBJECT::[dbo].[AspNetUserClaims] TO [manutencao_runtime];
GRANT SELECT ON OBJECT::[dbo].[AspNetRoleClaims] TO [manutencao_runtime];
GRANT SELECT, INSERT, UPDATE ON OBJECT::[dbo].[Tickets] TO [manutencao_runtime];
GRANT SELECT, INSERT ON OBJECT::[dbo].[TicketEvent] TO [manutencao_runtime];
GRANT SELECT, INSERT, DELETE ON OBJECT::[dbo].[Sessions] TO [manutencao_runtime];
GO
