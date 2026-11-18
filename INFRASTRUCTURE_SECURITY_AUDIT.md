# Auditoria de Segurança da Infraestrutura Docker

**Projeto:** Manutenção em Campo  
**Coleta:** 18/11/2026, host Windows local  
**Escopo:** Docker Desktop, imagem SQL Server fixada, implantação ASP.NET Core, TLS, chaves, sessões, operação e recuperação. Complementa [SECURITY_AUDIT.md](SECURITY_AUDIT.md), onde estão as evidências das três fases anteriores.

## 1. Resumo executivo

**A infraestrutura de produção ainda não pode ser aprovada.** O Compose é um perfil de desenvolvimento com SQL Server Developer. No host auditado havia a imagem prevista, mas nenhum contêiner do projeto e nenhum processo Windows identificado como painel, Expo ou legado. A inspeção comprova a configuração local declarada, o inventário do Engine e o conteúdo da imagem. A topologia, os certificados, as credenciais efetivas e os controles operacionais de produção seguem sem evidência.

A imagem local coincide com o digest do Compose. A varredura offline identificou **45 advisories distintos em 106 ocorrências**. Três ocorrências altas pertencem a um único CVE em três binários Go. A versão afetada está presente. A exposição efetiva do CVE no fluxo SQL do projeto permanece indeterminada. A etiqueta oficial da imagem indica SQL Server 2022 CU28. [Histórico do SQL Server 2022 para Linux](https://learn.microsoft.com/en-us/troubleshoot/sql/releases/linux/release-history-2022) · [Advisory Go GO-2026-6809](https://pkg.go.dev/vuln/GO-2026-6809)

Antes da implantação, as prioridades são definir a topologia e a edição SQL de produção, validar certificados e identidades no destino, testar recuperação de dados e chaves e implantar coleta de segurança. Os controles SEC-01 a SEC-12 permanecem regidos pelo relatório anterior. Esta auditoria acrescenta achados de infraestrutura.

## 2. Inventário e ambientes

| Componente | Evidência da coleta ou configuração declarada |
| --- | --- |
| Host Windows | Build `26300.9974`. Nenhum processo identificado pelos nomes e argumentos do projeto. Nenhum listener encontrado nas portas `14333`, `5180`, `7180`, `8081`, `2375` ou `2376` no instante da inspeção. O teste de listeners foi local e passivo. |
| Docker Desktop | 4.99.0, contexto `desktop-linux`. API pelo named pipe `dockerDesktopLinuxEngine`. Engine 29.9.3/API 1.56, Compose 5.7.0, containerd 2.4.2, runc 1.5.2, VM Linux x86_64, overlay2, cgroup v2, seccomp builtin, namespace de cgroups, logging padrão `local`, Swarm inativo e live-restore desabilitado. |
| Inventário global do Engine | Sete contêineres, todos parados. Cinco redes locais, três bridge, uma host e uma null. Quatro volumes com driver local. Nenhum recurso com o rótulo do projeto esperado e nenhum contêiner com o rótulo Compose `service=sqlserver`. Os recursos de terceiros foram apenas contabilizados. |
| SQL Server | Único serviço do [Compose](compose.yaml). Imagem Microsoft SQL Server 2022 para `linux/amd64`. Porta declarada `127.0.0.1:14333 → 1433/tcp`. Bridge padrão do Compose e volume nomeado `sql-data`. Etiqueta da imagem `com.microsoft.version=16.0.4325.4` e usuário padrão `mssql`. |
| ASP.NET Core e SQLite | O [script de demonstração](scripts/Start-Painel.ps1#L17) roda painel MVC e API no Windows, em `Development`, com demo HTTP explícita e bind em `localhost:5180`. SQLite é a opção local padrão. O perfil de lançamento alternativo usa `https://localhost:7180`. A aplicação usa SQL Server fora do perfil SQLite de Development. |
| Expo e legado | O Expo web roda fora do Docker, no navegador, com servidor de desenvolvimento em `localhost:8081`. O cliente .NET Framework 4.8 é executado no Windows. Ambos acessam a API. A origem configurada para a versão distribuída exige HTTPS. [README](README.md) |
| Produção | O repositório documenta requisitos para Kestrel, proxy, hosts e origem Expo. O inventário não encontrou manifesto de implantação da aplicação, proxy, destino de logs ou infraestrutura de produção. Isso não identifica serviços eventualmente mantidos em outro repositório ou provedor. |

## 3. Arquitetura lógica observada

O diagrama representa os limites de confiança que o repositório estabelece para o ambiente local. Trata-se da topologia declarada. A coleta encontrou apenas o Engine e a imagem em repouso.

~~~mermaid
flowchart LR
    subgraph Host["Host Windows"]
        Browser["Navegador / Expo web"]
        Legacy["Cliente legado .NET Framework"]
        App["Kestrel · MVC e API"]
        File["SQLite local · demo padrão"]
        Admin["Processo separado de migrations"]
        Browser -->|"HTTP loopback na demo ou HTTPS"| App
        Legacy -->|"HTTP loopback na demo ou HTTPS"| App
        App -->|"Demo SQLite"| File
    end
    subgraph VM["VM Linux do Docker Desktop"]
        direction TB
        Sql["SQL Server Developer · bridge Compose"]
        Data["Volume sql-data"]
        Mounts["Configuração TLS + certificado e chave · ro"]
        Sql --> Data
        Mounts --> Sql
    end
    App -->|"TLS SqlClient · loopback 14333 → 1433"| Sql
    Admin -->|"TLS e identidade distinta"| Sql
~~~

Uma aplicação Windows que se conecta ao SQL publicado cruza o limite Windows–VM, mesmo usando loopback. A API do Docker é outro limite de confiança. Quem pode administrar o daemon pode inspecionar metadados e operar recursos associados à VM. Para produção falta validar os limites entre cliente, borda, Kestrel, rede privada e banco. [Permissões no Docker Desktop para Windows](https://docs.docker.com/desktop/setup/install/windows-permission-requirements/) · [Publicação de portas](https://docs.docker.com/engine/network/port-publishing/)

## 4. Verificações e resultados

**Conforme** indica controle demonstrado no escopo da linha. **Não conforme** indica requisito ausente ou violado no escopo indicado. **Risco potencial** indica exposição condicionada a uso ou implantação. **Não verificado** indica falta de evidência suficiente. **Não aplicável** indica controle sem objeto naquela topologia.

### Docker, host e cadeia de imagem

| ID | Verificação | Resultado | Evidência técnica |
| --- | --- | --- | --- |
| D01 | Integridade do pin local | Conforme | O `RepoDigests` corresponde ao pin. O ID da imagem, o SHA-256 do JSON de configuração extraído do archive e o `ImageID` do scanner coincidem. Ver seção 7. |
| D02 | Digest e imagem em execução | Não verificado | Nenhum contêiner do projeto. Na fase 3 o digest foi usado em um contêiner descartável, como atesta o [registro anterior](SECURITY_AUDIT.md#verificações). |
| D03 | Usuário, privilégios e isolamento | Risco potencial | Configuração da imagem declara `mssql`. O Compose não solicita `privileged`, host PID, host network ou socket Docker. O Engine anunciou seccomp builtin e cgroup namespace. O Compose não fixa UID/GID, `cap_drop`, `no-new-privileges` ou filesystem raiz somente leitura. UID efetivo, capacidades, perfil de segurança, mapeamento de usuários e compatibilidade de endurecimento não puderam ser medidos em execução. |
| D04 | Porta, rede e segmentação | Conforme | `docker compose config` resolveu IPv4 loopback para 14333 e a rede padrão do projeto. O snapshot do host não encontrou listener de banco. A bridge não foi criada nesta etapa. Segmentação, egress e firewall de produção seguem sem verificação. |
| D05 | Montagens e armazenamento | Conforme | Configuração resolve volume nomeado gravável em `/var/opt/mssql` e três binds com `read_only=true`, incluindo chave e certificado. A escrita no dado SQL independe dos binds de TLS. |
| D06 | Proteção do volume e dos arquivos TLS | Não verificado | Sem volume atribuído ao projeto ou caminho TLS configurado no processo auditado. Permissões efetivas Windows e Linux, titularidade, criptografia do disco/volume e acesso a snapshots não medidos. Um bind `ro` protege contra escrita pelo contêiner, mas permite leitura segundo as permissões efetivas. |
| D07 | CPU, memória, PIDs e reinício | Não conforme | A configuração resolvida não estabelece limites por serviço nem política de reinício. Engine com live-restore desabilitado. Orçamento de recursos e recuperação após falha não demonstrados. |
| D08 | Healthcheck e prontidão | Não conforme | O probe do Compose apenas abre TCP em `127.0.0.1:1433` dentro do contêiner. Ele não confirma TLS, login nem uma consulta no banco alvo. Não consome uma conta SQL, o que atende à finalidade básica da demo. |
| D09 | Conteúdo da imagem | Não conforme | Trivy offline detectou 106 ocorrências e três ocorrências altas do mesmo advisory no conteúdo fixado. Consultar seção 7 e INF-01. |
| D10 | Segredo administrativo de bootstrap | Risco potencial | Manifesto injeta `MSSQL_SA_PASSWORD` no ambiente do contêiner. Identidades com acesso ao Docker podem ler a configuração da instância. Ausência de contêiner impediu confirmar o estado de `sa` após bootstrap e a política de rotação. Apenas o nome da variável foi coletado. |
| D11 | Exposição e acesso ao Engine | Conforme | Contexto usa named pipe local. Portas TCP convencionais 2375/2376 sem listener. O grupo Windows `docker-users` tinha um membro no momento da coleta. O número de membros não comprova ACL efetiva do pipe, administração delegada ou ausência de outros canais. |
| D12 | Patches do host e isolamento adicional | Risco potencial | Docker Desktop observado empacota Engine 29.9.3. Existe Engine 29.10.0 com mudanças de segurança e runc 1.5.3. A aplicabilidade de CVE-2026-119101 depende do uso de checkpoint/restore com imagens não confiáveis, sem evidência desse fluxo no projeto. Política de atualização, ACL dos endpoints, ECI e configuração administrativa da VM não verificados. [Versão Desktop](https://docs.docker.com/desktop/release-notes/#4990) · [Engine](https://docs.docker.com/engine/release-notes/29/#29100) · [runc 1.5.3](https://github.com/opencontainers/runc/releases/tag/v1.5.3) |
| D13 | App, Expo e legado containerizados | Não aplicável | O Compose contém somente `sqlserver`. Os componentes Windows e do navegador estão fora do Docker nesta arquitetura. |

### SQL Server

| ID | Verificação | Resultado | Evidência técnica |
| --- | --- | --- | --- |
| S01 | Edição destinada à produção | Não conforme | Compose resolve `MSSQL_PID=Developer`. É uma edição de desenvolvimento e testes. Definir o destino licenciado ou gerenciado antes de implantação real. [Edições Microsoft SQL Server 2022](https://learn.microsoft.com/en-us/sql/sql-server/editions-and-components-of-sql-server-2022) |
| S02 | Identidades e privilégio mínimo declarado | Conforme | [SqlServerPolicy.cs](src/ManutencaoEmCampo.Web/Data/SqlServerPolicy.cs#L11) separa a configuração de runtime da de migrations, rejeita `sa` no runtime e verifica privilégios específicos na primeira abertura. [runtime-permissions.sql](sql/runtime-permissions.sql) define grants por objeto e sem DDL. Em laboratório nas fases anteriores, runtime restrito funcionou e identidade de migrations foi rejeitada no runtime. |
| S03 | Grants e isolamento efetivos | Não verificado | Nenhum serviço local ativo nem identidade de produção apresentada. A checagem de boot é parcial e por processo, sem substituir revisão de grupos, roles fixas, `public`, grants diretos e permissões alteradas depois da primeira conexão. Falta comprovar também que o job de migrations injeta suas credenciais somente no processo autorizado. |
| S04 | Configuração SQL de criptografia | Conforme | [mssql-tls.conf](sql/mssql-tls.conf) contém `forceencryption=1`. Driver rejeita `Encrypt=Optional` e `TrustServerCertificate=true`. A fase 3 testou aceitação da configuração de confiança e recusa de certificado divergente no laboratório. [Opções de conexão do SqlClient](https://learn.microsoft.com/en-us/sql/connect/ado-net/connection-options) |
| S05 | Certificado e protocolos SQL efetivos | Não verificado | Conjunto de protocolos, cadeia ou pin, nome, validade, acesso da identidade SQL, rotação e criptografia observada por sessão dependem da instância e do certificado definitivos. `forceencryption` não prova esses itens. |
| S06 | Integridade do modelo e migrations | Conforme | EF Core 10.0.15 listou `InitialCreate` e `TrackActiveSessions` com `--no-connect --no-build`. `has-pending-model-changes` confirmou ausência de mudança pendente. Conferência por hash ligou fontes de migrations/contexto ao artefato da fase 3. A lista não informa quais migrations estão instaladas no destino. |
| S07 | Reinício e persistência do SQL | Não verificado | Volume nomeado declarado. A persistência após reinício e o estado do `__EFMigrationsHistory` exigem uma instância descartável autorizada. |
| S08 | Backup, restauração e DR | Não conforme | Não foi encontrado procedimento de backup validado, retenção, destino fora do host, RPO/RTO ou resultado de restauração. Disponibilidade de política externa também não verificada. O volume fornece persistência local. [Restauração SQL Server em contêiner](https://learn.microsoft.com/en-us/sql/linux/migrate/tutorial-restore-backup-sql-server-container) |

### ASP.NET Core, HTTPS, chaves e sessões

| ID | Verificação | Resultado | Evidência técnica |
| --- | --- | --- | --- |
| A01 | Obrigatoriedade de TLS e HSTS | Conforme | [Program.cs](src/ManutencaoEmCampo.Web/Program.cs#L144) exige handshake TLS real fora da exceção local, retorna 400 para HTTP e aplica HSTS fora de Development. O histórico contém teste com HTTPS e HSTS. HTTP→HTTPS, se adotado, deve ser comprovado na borda. |
| A02 | TLS, hosts e configuração efetiva de Production | Não verificado | Nenhum listener do projeto foi encontrado. Faltam identificação do serviço, certificado e cadeia, versões TLS, portas, configurações efetivas, política HSTS da borda e evidência de filtragem do site implantado. Arquivo base não define endpoint TLS nem hosts de produção. |
| A03 | Reverse proxy, forwarded headers e host | Conforme | [Program.cs](src/ManutencaoEmCampo.Web/Program.cs#L20) desativa forwarding genérico. [TrafficPolicy.cs](src/ManutencaoEmCampo.Web/Data/TrafficPolicy.cs#L42) só admite IPs explícitos de proxies e um salto de `X-Forwarded-For`. `X-Forwarded-Host` e `X-Forwarded-Proto` não são usados nessa política. [TransportPolicy.cs](src/ManutencaoEmCampo.Web/Data/TransportPolicy.cs#L7) exige hosts exatos. Proxy definitivo precisa passar TLS ou estabelecer TLS ao Kestrel, preservar um `Host` aprovado e controlar o `X-Forwarded-For`. [Guia de reverse proxy](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0) |
| A04 | Superfície administrativa e de desenvolvimento | Conforme | OpenAPI é mapeado em Development. O seed SQL exige modo demo em Development e operação explícita. Não foi identificado endpoint de administração de infraestrutura. Os controladores com papéis gerenciam chamados e sessões. Não houve varredura de rotas do ambiente implantado. |
| A05 | Cookies, bearer e revogação | Conforme | Cookie seguro fora da demo HTTP, antifalsificação, bearer com até 30 min, sessão MVC de até 8 h e revalidação de conta, carimbo e papéis nas requisições. Logout revoga a sessão usada. CORS de origem explícita e `no-store` permanecem ativos conforme código. [Program.cs](src/ManutencaoEmCampo.Web/Program.cs#L67) · [ActiveSessions.cs](src/ManutencaoEmCampo.Web/Data/ActiveSessions.cs) |
| A06 | Chaves de Data Protection em demonstração | Conforme | Registro em memória por processo via [DemoKeyRepository.cs](src/ManutencaoEmCampo.Web/Data/DemoKeyRepository.cs) e [Program.cs](src/ManutencaoEmCampo.Web/Program.cs#L30). Perder o processo implica perder o material criptográfico da demo. Uma linha de sessão persistida no banco não substitui a chave que protege a credencial. |
| A07 | Repositório e proteção das chaves fora da demo | Não verificado | O código usa descoberta padrão do framework. Há um diretório de chaves no perfil Windows do processo de auditoria, sem atribuição ao serviço do projeto. Nenhum conteúdo foi aberto. Perfil do serviço, ACL, proteção efetiva em repouso, rotação, backup e restauração não demonstrados. Com perfil Windows adequado o padrão pode usar DPAPI. Um repositório explícito exige escolher proteção em repouso. [Padrões Data Protection](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/default-settings?view=aspnetcore-10.0) · [Proteção em repouso](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-encryption-at-rest?view=aspnetcore-10.0) |
| A08 | Chaves e sessões após reinício ou em réplicas | Não verificado | Não há key ring compartilhado, discriminador de aplicação explícito ou topologia de réplicas demonstrados no material. Testar cookie, bearer, antifalsificação e revogação na mesma instalação após reinício e cruzando réplicas. A linha de sessão no SQL e as chaves são dependências distintas, e ambas afetam o resultado. |

### Monitoramento e disponibilidade

| ID | Verificação | Resultado | Evidência técnica |
| --- | --- | --- | --- |
| O01 | Emissão e minimização dos eventos de segurança | Conforme | [SecurityEvents.cs](src/ManutencaoEmCampo.Web/Data/SecurityEvents.cs#L25) emite eventos estruturados 4600–4608, inclui correlação e incrementa o contador `manutencao.security.events`. Configuração base usa JSON. A fase 2 verificou que os eventos da suíte não continham senhas, tokens, e-mails ou IPs de entrada. Essa evidência não cobre os logs do proxy, do host ou do SQL. |
| O02 | Coleta, acesso, retenção e alertas | Não conforme | Nenhum coletor, política de retenção, destino durável ou alerta foi evidenciado. O driver Docker `local` observado pertence ao Engine e não coleta o processo ASP.NET Core que roda no Windows. Persistência e acesso ao log real de produção não verificados. |
| O03 | Rate limiting em múltiplas réplicas | Risco potencial | Limitadores em memória por processo. O padrão por minuto é 60 logins por origem, 8 por conta/origem, 120 operações e 40 listagens por conta. Há teto de corpo de 32 KiB. Em réplicas, o orçamento efetivo depende do balanceamento e os contadores reiniciam com o processo. Validação de sessões autenticadas pode consultar o banco antes de `UseRateLimiter`, reforçando a necessidade de controles na borda. [Program.cs](src/ManutencaoEmCampo.Web/Program.cs#L89) · [TrafficPolicy.cs](src/ManutencaoEmCampo.Web/Data/TrafficPolicy.cs) |
| O04 | Usuários atrás de NAT ou proxy | Risco potencial | A cota de login por origem é compartilhada quando vários usuários têm o mesmo IP de saída. Proxy sem confiança configurada pode agrupar toda a clientela sob um único endereço. Validar `429/Retry-After`, distribuição das cotas e carga legítima no perfil real. |
| O05 | Recuperação e disponibilidade do serviço | Não verificado | Não há evidência de exercício operacional de failover, alerta ou recuperação do host. Backups SQL e recuperação de chaves precisam de um exercício coordenado. |

## 5. Vulnerabilidades confirmadas e lacunas

As severidades abaixo consideram o impacto da promoção da configuração atual para produção. Para CVEs, a severidade do scanner e a aplicabilidade observada são explicitadas.

| Achado | Severidade e estado | Impacto |
| --- | --- | --- |
| **INF-01 · Dependência Go afetada na imagem** | **Alta no componente**, pelo Trivy. Risco potencial no serviço. | CVE-2026-111943 detectado em `launchpad`, `launchpadd` e `setnetbr`, os três com Go 1.27.2. O advisory trata de confiança em certificados intermediários revogados. A presença da versão foi confirmada pela varredura do archive. O uso do caminho TLS afetado por esses binários no projeto não foi demonstrado. Exigir digest atualizado ou avaliação documentada da Microsoft antes de promoção. [Advisory Go](https://pkg.go.dev/vuln/GO-2026-6809) |
| **INF-02 · Perfil Developer inadequado para produção** | **Alta operacional**, lacuna confirmada no manifesto. | A edição configurada é voltada a desenvolvimento e testes. O mesmo perfil não deve ser o artefato de produção. |
| **INF-03 · Recuperação sem evidência operacional** | **Alta operacional**, lacuna confirmada no material versionado. | Falha do host, corrupção ou erro operacional podem causar perda de disponibilidade e dados. Um retorno a backup anterior à revogação pode restaurar registros de sessão ainda válidos se a credencial correspondente continuar decifrável. Planejar invalidação de sessões na recuperação e testar. |
| **INF-04 · Prontidão e limites do SQL incompletos** | **Média**, configuração confirmada. | Porta aberta pode sinalizar saúde enquanto o banco está indisponível. Ausência de limites e reinício explícitos amplia impacto de falhas de recurso ou de processo. |
| **INF-05 · Segredo de bootstrap na configuração Docker** | **Média**, propriedade confirmada no manifesto. | Acesso administrativo ao Engine permite consultar a configuração do contêiner. O alcance depende de quem acessa o daemon e do estado e rotação da credencial de bootstrap. Não foi observado vazamento de valores. |
| **INF-06 · Continuidade criptográfica sem evidência** | **Média**, risco potencial. | Troca de identidade, host ou instância pode invalidar cookies/bearer. Armazenar chaves com permissões amplas aumentaria exposição de material criptográfico. |
| **INF-07 · Telemetria e cotas sem controle de frota demonstrado** | **Média**, lacuna de implantação e risco potencial. | Incidentes podem não gerar alerta. Réplicas e redes compartilhadas podem diluir ou concentrar o limite, com efeitos sobre proteção e disponibilidade. |

O CVE do runc aparece como risco condicionado em D12. Não houve comprovação de checkpoint/restore nem de comprometimento do host. Não há achado crítico confirmado nesta coleta. A cobertura limitada impede uma conclusão global de ausência de vulnerabilidades.

## 6. Riscos residuais

- A imagem fixa o artefato conhecido e também preserva seus advisories até que o digest seja atualizado. O uso dos binários Go com o caminho TLS afetado e a presença de correções adicionais da Microsoft exigem triagem antes da promoção.
- Loopback limita a publicação local. Outros processos autorizados no host e entidades no plano de controle do Docker continuam dentro do limite de confiança. O controle efetivo muda se o SQL e a aplicação forem separados entre hosts.
- Sem ensaio de recuperação, o RPO/RTO, a continuidade de autenticação e a invalidação de sessões após restaurar o SQL são desconhecidos.
- O risco residual SEC-10 do Expo permanece documentado na fase 3. A varredura da imagem SQL cobre outro artefato.

## 7. Evidências técnicas

### Imagem e advisories

- Referência fixada `mcr.microsoft.com/mssql/server:2022-latest@sha256:4525e3efac9f3e241cb003bb2ff2a82818b502f37e12049d788b1a9fa7e13b7f`.
- ID de configuração `sha256:548ecf4a83632020d95dfc744bac431f91e31458c2a785b61275941abae7c2bf`. Validado com `docker image inspect`, o JSON de configuração do archive e Trivy. Base detectada Ubuntu 22.04. Etiqueta `com.microsoft.version` indica `16.0.4325.4`, associado a CU28/KB5125472. Confirmar a edição e o build em execução com `SERVERPROPERTY` quando houver instância. Trivy não verificou a versão do motor SQL pelo banco de pacotes do Ubuntu. [Histórico SQL Server](https://learn.microsoft.com/en-us/troubleshoot/sql/releases/linux/release-history-2022)
- Trivy 0.76.2, SHA-256 do ZIP `691f737a7e5566a673c3a74dab9160552ec32101e953b659e5c77443d16dddb6`. Base oficial schema 2, carimbo `2026-11-18T19:07:09Z`. Varredura offline de pacotes do SO e bibliotecas, sem upload da imagem. [Publicação do Trivy](https://github.com/aquasecurity/trivy/releases/tag/v0.76.2) · [Método e limitações de detecção](https://github.com/aquasecurity/trivy/blob/main/docs/guide/scanner/vulnerability.md)
- Resultado por ocorrência tem 0 críticas, 3 altas, 64 médias, 37 baixas e 2 de severidade desconhecida. São 98 ocorrências em pacotes do SO e 8 em binários Go, 106 ocorrências agrupadas em 45 IDs. 33 ocorrências têm versão de correção indicada. O status `fixed` do feed indica que há correção publicada, com a versão atualmente instalada registrada em separado.
- Arquivos de evidência da coleta local incluem `compose-sanitized.json`, `windows-sanitized.json` e `image-findings-sanitized.json`. SHA-256 do último `2c975e5cd2a65ec5ef8bb0f6f16d746e8ba5af51d7f96e57678aad6a87dd23f3`. A varredura de pacotes não valida assinaturas, os CVEs do runtime Docker, a efetiva execução de um caminho de código ou a configuração de uma instância ativa.

### Comandos de reprodução

A inspeção do Engine usou `docker context show`, `docker version`, `docker info`, `docker ps -a`, `docker network ls`, `docker volume ls` e `docker image inspect`. Saídas foram restritas a versões, contagens, rótulos do projeto e propriedades específicas. A configuração resolvida foi examinada com `docker compose config --format json` usando variáveis fictícias e exportando somente os campos de interesse. Para evitar exposição de valores reais, filtre a saída ao repetir em outro ambiente.

A varredura usou `docker image save` na referência fixada e `trivy image --input <archive> --offline-scan --skip-db-update --skip-java-db-update --scanners vuln --pkg-types os,library` após baixar a base oficial. O EF Core usou `migrations list --no-connect --no-build` e `migrations has-pending-model-changes --no-build` no artefato Release da fase anterior. Nenhuma dessas verificações precisou de conexão SQL.

## 8. Limitações do ambiente

- O escopo efetivo é um host de desenvolvimento com Docker Desktop. O relatório não certifica uma infraestrutura de produção que não foi fornecida.
- Nenhum endpoint externo foi testado. Houve consulta a documentação pública e download autorizado da ferramenta e da base de advisories.
- Até o fechamento desta coleta não havia uma instância de teste ativa. Foi solicitada autorização para uma nova instância descartável, isolada em loopback, com dados e credenciais fictícios, reinício apenas dessa instância e preservação dos recursos parados ao final. Sem essa autorização, persistência, backup, restauração, grants e TLS efetivos permanecem pendentes nesta fase. As evidências anteriores aparecem identificadas como históricas.
- Não foram inspecionados valores de variáveis, arquivos de chave ou informações de conexão existentes. Nenhum serviço existente foi interrompido, nenhum contêiner ou volume foi removido e nenhuma migration foi executada em banco existente.

## 9. Plano priorizado de remediação

| Prioridade | Ação proposta | Evidência para concluir |
| --- | --- | --- |
| **P0 · Antes da implantação** | Definir infraestrutura, edição SQL, redes privadas, identidade da aplicação, job de migrations e origem web. Preservar TLS até o Kestrel e para o SQL. | Inventário aprovado, regras de rede e endpoints efetivos, testes de TLS, host e proxy no ambiente autorizado. |
| **P0 · Antes da implantação** | Resolver ou justificar INF-01 com build da Microsoft, novo digest reavaliado e política de aprovação da imagem. Revisar a cadência de atualização do Engine e manter checkpoint/restore fora do procedimento até confirmar a correção ou inaplicabilidade. | Scan atualizado, registro de exceções com prazo e artefato executado com digest correspondente. |
| **P0 · Antes de manter dados reais** | Definir backup fora do host, criptografia, retenção, RPO/RTO, restauração e tratamento das sessões após recuperação. Definir key ring persistente com proteção explícita e identidade de serviço, adequado ao número de réplicas. | Exercício isolado com restauração, verificação de integridade, tempo medido, checagem de ACL e revogação em reinício e entre réplicas. |
| **P1 · Antes da abertura de tráfego** | Revisar privilégios efetivos de SQL e Docker, acesso à chave TLS, segregação da credencial de bootstrap, política de reinício, memória/CPU/PIDs e readiness. | Grants auditados, limites e isolamento inspecionados em execução, login e consulta com credenciais mínimas para readiness e testes de falha. |
| **P1 · Antes da abertura de tráfego** | Implantar coleta de logs e métrica, retenção, alertas e controles de tráfego na borda. Calibrar cotas para NAT e réplicas. | Simular eventos benignos, observar entrega de alerta, validar expurgo e redaction, executar carga autorizada de homologação. |

## 10. Checklist de produção

- [ ] Aprovar inventário, owner, rede, edição SQL e modelo de operação do host.
- [ ] Confirmar digest em execução, advisories atuais, origem e política de atualização da imagem e do Engine.
- [ ] Confirmar usuário efetivo, capabilities, perfis de isolamento, mounts, ACL, criptografia e donos dos dados.
- [ ] Restringir Engine e SQL à administração e às redes previstas, incluindo teste da exposição IPv4/IPv6 no ambiente autorizado.
- [ ] Aplicar limites de CPU, memória e PIDs, readiness SQL, alertas de saúde e estratégia de reinício.
- [ ] Provar runtime sem administração e migrations com identidade separada, inclusive após atualização.
- [ ] Verificar TLS no cliente, borda, Kestrel e SQL, cadeias/nomes, protocolos, HSTS, hosts e encaminhamento de IP.
- [ ] Testar key ring protegido e recuperável, rotação, cookies, bearer, antifalsificação e revogação após reinício e entre réplicas.
- [ ] Demonstrar entrega, acesso e retenção de logs e métricas, com alertas acionáveis e sem segredos.
- [ ] Validar as cotas com NAT e múltiplas réplicas e testar restauração de backups com metas de RPO/RTO.


## Quinta etapa · Validação dinâmica e remediação local

Validação em 18/11/2026 no Docker Desktop Windows, com Engine Linux. O projeto de teste `maintenance-infra5-bdcdfd7c4ed1` recebeu rede e volume novos, bind em `127.0.0.1` com porta atribuída pelo Docker, chaves e credenciais fictícias e banco exclusivamente de laboratório. A execução conferiu o contexto, o nome, os rótulos, o ID e a imagem dos recursos antes de operá-los. Ao final, a comparação de IDs, estados e nomes confirmou o inventário preexistente preservado. A seção anterior permanece como registro da auditoria inicial de infraestrutura.

### Estado dos achados

| Achado | Estado e evidência da quinta etapa |
| --- | --- |
| **INF-01 · Imagem SQL** | **Aberto.** O Microsoft Artifact Registry retornou o mesmo digest para `2022-latest` e `2022-CU28-ubuntu-22.04`. O catálogo consultado não oferecia CU posterior na mesma linha Ubuntu 22.04. O processo executado confirmou SQL Server Developer `16.0.4325.4`. A troca por SQL Server 2025 implica uma mudança de versão principal que exige decisão e validação de compatibilidade, inclusive restauração e rollback. Mantido o digest atual. [Tags oficiais](https://mcr.microsoft.com/v2/mssql/server/tags/list) · [Histórico SQL Server 2022 para Linux](https://learn.microsoft.com/en-us/troubleshoot/sql/releases/linux/release-history-2022) |
| **INF-02 · Edição SQL** | **Estratégia documentada.** Developer segue no perfil local. A edição de produção será escolhida após levantamento de licenciamento, capacidade, funcionalidades e operação. Podem ser avaliados SQL Server licenciado ou serviço gerenciado. Nenhuma implantação ou aquisição ocorreu nesta etapa. [Edições SQL Server 2022](https://learn.microsoft.com/en-us/sql/sql-server/editions-and-components-of-sql-server-2022) |
| **INF-03 · Recuperação** | **Procedimento local validado.** [O runbook](sql/RECOVERY.md) cobre backup com checksum, verificação, restauração em banco novo, integridade e invalidação de sessões. Persistem os requisitos de backup criptografado fora do host, retenção, RPO/RTO e exercício de recuperação em homologação. [Restauração em outro banco](https://learn.microsoft.com/en-us/sql/relational-databases/backup-restore/restore-a-database-to-a-new-location-sql-server) |
| **INF-04 · Prontidão e limites** | **Corrigido no perfil local.** O healthcheck verifica CA e nome no TLS obrigatório e consulta o banco configurado com identidade exclusiva. A indisponibilidade do banco alvo produziu `unhealthy` mesmo com o SQL aceitando conexões para `master`. O serviço recuperou `healthy`. O cgroup impôs 2 CPUs, 3 GiB e 512 tarefas. O Compose configura o teto interno SQL de 2 GiB e até três tentativas de reinício por falha do processo. O laboratório provocou uma falha e confirmou o primeiro reinício automático. |
| **INF-05 · Bootstrap** | **Superfície reduzida e verificada.** Senhas fictícias foram fornecidas por arquivos montados como secrets de leitura. A inspeção confirmou que nenhuma aparece em `Config.Env`. O processo de bootstrap ainda precisa ler a senha e pode expô-la a administradores do Engine. A chave e o certificado são copiados como `0400` sob `tmpfs` `0700`, pertencente ao UID/GID 10001. As permissões dos arquivos no host e a desativação ou rotação administrativa após o provisionamento permanecem sob responsabilidade da operação. [Docker Compose secrets](https://docs.docker.com/compose/how-tos/use-secrets/) · [TLS do SQL Server no Linux](https://learn.microsoft.com/en-us/sql/linux/security/encrypted-connections) |
| **INF-06 · Data Protection** | **Proposta documentada.** A estratégia depende do host e do número de réplicas. O teste de rollback manteve o processo e as chaves de demo para demonstrar o efeito nos tokens. A configuração real do key ring permanece pendente. |
| **INF-07 · Observabilidade e cotas** | **Requisitos documentados.** Ficaram pendentes seleção de destino, retenção, entrega de alertas, política compartilhada de tráfego e validação distribuída. |

### Testes dinâmicos e recuperação

| Verificação | Resultado |
| --- | --- |
| Identidade, montagens e recursos | UID e GID `10001` (`mssql`), `CapEff=0`, `Seccomp=2` e contêiner sem modo privilegiado. Cgroup v2 registrou `cpu.max=200000 100000`, `memory.max=3221225472` e `pids.max=512`. Rótulo e ID da imagem em execução corresponderam ao fixture. |
| Privilégios SQL e migrations | Runtime sem `sysadmin`, `db_owner` ou DDL e com grants por objeto. O executor de migrations tinha `db_owner` apenas no banco descartável e aplicou as duas migrations. O app recusou essa identidade no runtime. A conta de prontidão consultou o banco sem poder ler chamados ou criar tabelas. |
| TLS | SQL restringido a TLS 1.2. A conexão administrativa reportou criptografia ativa. O healthcheck recusou nome divergente. O aplicativo aceitou a conexão com o certificado de teste esperado e recusou arquivo de certificado diferente. O teste de TDS 8.0 com os certificados do laboratório foi rejeitado pelo servidor. A configuração validada usa criptografia obrigatória e validação do certificado. |
| Persistência e falhas | Reiniciar somente o contêiner de teste preservou 5 chamados fictícios e as 2 migrations. O banco foi colocado offline e online no laboratório. O estado observado foi `healthy → unhealthy → healthy`. Na falha controlada do processo SQL de teste, o contador do Docker subiu de 0 para 1. Após o reinício automático, o probe voltou a `healthy` e o histórico de migrations continuou íntegro. |
| Backup e restauração | `BACKUP DATABASE ... WITH COPY_ONLY,CHECKSUM` e `RESTORE VERIFYONLY WITH CHECKSUM` concluídos. O backup foi restaurado em banco novo com `MOVE`. `DBCC CHECKDB` passou. Foram restaurados 6 chamados, 16 eventos e 6 sessões, com 2 migrations no histórico. A restauração e a verificação levaram 1,44 s no laboratório. SHA-256 da cópia de teste `7071e8bcf88e85768a69bf4f68d3917bb03dbe3834c3e7d592bcc5dd90a0f426`. |
| Sessões após rollback | As credenciais de teste bearer e MVC estavam ativas antes do backup e foram invalidadas na base original. Ao restaurar deliberadamente o ponto anterior com a mesma aplicação e chaves, voltaram a ser aceitas. A limpeza de `dbo.Sessions` antes de reabrir o acesso fez o bearer retornar 401 e o MVC voltar ao login. Nova autenticação retornou 200. O procedimento foi incorporado ao runbook. |

### Varredura da imagem executada e regressões

- Digest de registro `sha256:4525e3efac9f3e241cb003bb2ff2a82818b502f37e12049d788b1a9fa7e13b7f` e ID `sha256:548ecf4a83632020d95dfc744bac431f91e31458c2a785b61275941abae7c2bf`. O ID inspecionado no contêiner corresponde ao do archive que o Trivy 0.76.2 reescaneou offline com a base de 18/11/2026 às 19:07 UTC.
- Foram 106 ocorrências de 45 advisories distintos. Por ocorrência, 0 críticas, 3 altas, 64 médias, 37 baixas e 2 de severidade desconhecida. O CVE-2026-111943 segue nos binários `launchpad`, `launchpadd` e `setnetbr` construídos com Go 1.27.2. A presença da versão foi detectada. A exposição no caminho efetivamente usado pelo aplicativo não foi demonstrada. [Advisory Go](https://pkg.go.dev/vuln/GO-2026-6809)
- `Build-Verified.ps1` restaurou os locks, compilou servidor e .NET Framework 4.8 em Release com zero erros e avisos, executou TypeScript, exportou Expo web e aprovou os **19 testes de segurança e regressão**. A suíte usa bancos SQLite descartáveis e cobre a abertura de um esquema anterior. A demonstração SQLite passou em 2/2 e confirmou SQLite 3.54.0 e `integrity_check=ok`. No SQL Server real, 2/2 regressões passaram e o cliente legado consultou a API.
- NuGet não retornou advisories. npm manteve um advisory moderado de minimatch refletido em três entradas, com zero altos ou críticos. Os locks, arquivos de aplicação e suíte SEC-01 a SEC-12 foram preservados.

### Requisitos da implantação definitiva

- **Data Protection.** Para Windows com uma única instância, avaliar uma conta de serviço dedicada, diretório persistente com ACL restrita e proteção explícita das chaves com DPAPI no escopo da conta. Documentar a restrição de recuperação fora da conta e do perfil originais. Para réplicas, selecionar um repositório compartilhado e protegido por um certificado de proteção de chaves ou serviço gerenciado, com discriminador estável da aplicação, separado por ambiente, e acesso limitado às réplicas autorizadas. Ensaiar reinício, rotação, recuperação de chaves e compatibilidade de cookies, antifalsificação e bearer entre réplicas. [Configuração do Data Protection](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0) · [Proteção em repouso](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-encryption-at-rest?view=aspnetcore-10.0)
- **Monitoramento.** Coletar JSON e o contador `manutencao.security.events` do medidor `ManutencaoEmCampo.Security` com identidade de coleta e retenção definidas. Alertar sobre crescimento de 4601, 4603, 4604 e 4607, prolongamento de `unhealthy` e falhas de backup ou restauração. Validar entrega, acesso, retenção e remoção de dados sensíveis em logs de aplicação, SQL e borda.
- **Tráfego.** Selecionar a política na borda para combinar cotas por origem e conta, com orçamento consistente entre réplicas e controle de NAT. A aplicação preserva seus limites locais e a política de confiança em proxies. Validar repasse de IP, `Retry-After`, carga legítima e falha do componente que mantém a cota compartilhada.
- **Operação SQL.** Definir imagem aprovada, origem e revisão de CVEs, edição licenciada, cadeia e rotação TLS, proteção do armazenamento e recuperação fora do host. Ajustar os limites e a política de reinício à carga. Validar identidade de operação, migrations e administração no destino. As medidas locais e o ensaio de recuperação acima não dimensionam a infraestrutura real.

### Arquivos modificados

[compose.yaml](compose.yaml), [sql/mssql-tls.conf](sql/mssql-tls.conf), [sql/RECOVERY.md](sql/RECOVERY.md), [README.md](README.md) e este relatório. A área local de evidência contém os logs `restart-verified.log`, `build-final.log` e `sqlite-verified.log`, além de `mssql-findings-sanitized.json`. A cópia de backup e o banco que ela restaurou ficaram restritos ao volume descartável durante o ensaio.
