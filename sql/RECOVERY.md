# Backup e restauração do SQL Server

Este procedimento foi preparado para validação em um laboratório com dados fictícios. Sua adoção em produção depende de responsáveis, janela de manutenção, destino de backup, retenção, criptografia e metas de perda de dados e recuperação aprovados.

## Preparação do ensaio

- Use um projeto Compose exclusivo, com volume novo, porta de loopback, certificados próprios e dados fictícios. Confirme o rótulo `com.docker.compose.project`, o ID do contêiner e o nome do volume antes de qualquer operação.
- Separe as identidades de execução, migrations, prontidão e recuperação. A identidade que faz o backup e a restauração deve ser autorizada para essas operações. Use uma conexão TLS com validação do certificado e mantenha credenciais fora do histórico do terminal e dos logs.
- Escolha um caminho novo de backup gravável pelo SQL Server. Use uma cópia desse arquivo em armazenamento externo ao host para validar a recuperação de uma falha no host. A pasta no volume do laboratório serve somente para o ensaio.
- Registre o build do SQL Server, a versão das migrations, a hora do backup, o SHA-256 da cópia e o tempo de restauração. Para produção, cubra também backups diferenciais ou de log se as metas exigirem recuperação a um ponto no tempo.

## Backup com checksum e restauração em outro banco

Os nomes e caminhos abaixo são exemplos de laboratório. O banco de destino e os arquivos de destino devem ser novos.

~~~sql
BACKUP DATABASE [MaintenanceLab]
TO DISK = N'/var/opt/mssql/data/maintenance-lab-copy.bak'
WITH COPY_ONLY, CHECKSUM;

RESTORE VERIFYONLY
FROM DISK = N'/var/opt/mssql/data/maintenance-lab-copy.bak'
WITH CHECKSUM;

RESTORE FILELISTONLY
FROM DISK = N'/var/opt/mssql/data/maintenance-lab-copy.bak';
~~~

Use a lista de arquivos do backup para preencher os nomes lógicos de cada arquivo no `MOVE`. A verificação do backup é seguida de restauração real e de checagem do resultado.

~~~sql
RESTORE DATABASE [MaintenanceLabRestored]
FROM DISK = N'/var/opt/mssql/data/maintenance-lab-copy.bak'
WITH CHECKSUM, RECOVERY,
    MOVE N'MaintenanceLab' TO N'/var/opt/mssql/data/MaintenanceLabRestored.mdf',
    MOVE N'MaintenanceLab_log' TO N'/var/opt/mssql/data/MaintenanceLabRestored_log.ldf';

DBCC CHECKDB ([MaintenanceLabRestored]) WITH NO_INFOMSGS, ALL_ERRORMSGS;
~~~

Compare `dbo.__EFMigrationsHistory`, dados esperados de chamados, histórico e contas com o ponto escolhido. Verifique também o mapeamento de logins, a política de privilégios e a conectividade TLS. A identidade de recuperação deve entregar o banco validado para o processo de implantação.

## Invalidação de sessões na recuperação

Uma restauração pode repor sessões revogadas depois do backup quando a aplicação ainda consegue decifrar as credenciais antigas. Durante a recuperação, mantenha o banco restaurado fora do tráfego de todas as réplicas. Após confirmar a identidade do banco restaurado, limpe a tabela de sessões antes de reabrir o acesso.

~~~sql
USE [MaintenanceLabRestored];
BEGIN TRANSACTION;
DELETE FROM dbo.Sessions;
DECLARE @Removidas int = @@ROWCOUNT;
IF EXISTS (SELECT 1 FROM dbo.Sessions)
BEGIN
    ROLLBACK TRANSACTION;
    THROW 51000, 'Falha ao invalidar sessoes restauradas.', 1;
END;
COMMIT TRANSACTION;
SELECT @Removidas AS SessoesInvalidadas;
~~~

Valide que um bearer anterior recebe 401, um cookie anterior pede novo login e uma nova autenticação funciona. Registre os resultados e o tempo de recuperação. Nos testes, guarde as credenciais de sessão só na memória, sem salvá-las no relatório ou log. Garanta que o plano de recuperação inclui a política de backup e acesso do key ring de Data Protection. Para um incidente que envolva chaves, siga o procedimento de rotação/revogação aplicável à implantação.

[Microsoft · Restauração em outro banco ou diretório](https://learn.microsoft.com/en-us/sql/relational-databases/backup-restore/restore-a-database-to-a-new-location-sql-server).
