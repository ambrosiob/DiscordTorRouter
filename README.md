<p align="center">
  <img src="src/DiscordTorRouter/Assets/Brand/link.png" width="112" alt="Ícone do Discord Tor Router">
</p>

<h1 align="center">Discord Tor Router</h1>

<p align="center">
  Leve conexões escolhidas do Discord pela rede Tor sem transformar o computador inteiro em um proxy.
</p>

<p align="center">
  <img alt="Windows 11" src="https://img.shields.io/badge/Windows_11-0078D4?logo=windows11&logoColor=white">
  <img alt="Plataforma x64" src="https://img.shields.io/badge/plataforma-x64-6C3CE1">
  <img alt="Licença MIT" src="https://img.shields.io/badge/licença-MIT-2EA7EF">
</p>

## O que ele faz?

O Discord Tor Router permite escolher quais endereços usados pelo Discord devem passar pela rede Tor. Ele já vem preparado para proteger `gateway.discord.gg:443`, enquanto as demais conexões continuam usando a internet normalmente.

Isso oferece controle: você decide o que será protegido, sem afetar navegador, jogos ou outros aplicativos.

> [!IMPORTANT]
> O aplicativo não envia automaticamente todo o Discord pelo Tor. Chamadas, mídia, atualizações e outros serviços continuam na conexão direta até que seus endereços sejam adicionados à lista.

## Destaques

- Proteção ativada automaticamente ao abrir o aplicativo.
- Interface nativa e translúcida, integrada ao Windows 11.
- Menu completo na bandeja do sistema.
- Lista personalizável de endereços protegidos.
- Bloqueio preventivo: se o Tor falhar, a conexão protegida não escapa pela internet direta.
- Abertura do Discord somente quando a proteção estiver pronta.
- Nova identidade Tor em um clique.
- Inicialização opcional com o Windows.
- Salvamento automático das configurações.
- Nenhuma leitura de mensagens, tokens, senhas ou conteúdo do Discord.

## Instalação

1. Baixe `DiscordTorRouter-Setup.exe` na página de **Releases** do repositório.
2. Abra o instalador e siga as etapas.
3. Autorize a solicitação de administrador do Windows.
4. Ao iniciar, aguarde o status **Pronto** antes de usar o Discord.

O acesso de administrador é necessário para o aplicativo direcionar somente as conexões escolhidas.

### Requisitos

- Windows 11 de 64 bits.
- Conexão com a internet.
- Discord para Windows instalado.

O instalador completo inclui os componentes necessários para executar o aplicativo.

## Como usar

### Proteção automática

Abra o Discord Tor Router e aguarde. Ele resolve os endereços configurados, conecta ao Tor e prepara o roteador. Quando tudo estiver certo, o painel exibirá **Pronto**.

Se a opção de abrir o Discord estiver ligada, ele será iniciado somente depois dessa etapa.

### Adicionar endereços protegidos

Na seção **Endereços protegidos**, informe um destino por linha no formato `endereço:porta`:

```text
gateway.discord.gg:443
example.org:443
```

As mudanças são salvas automaticamente. Quando aparecer algo como **Protegidos: 5**, isso significa que os nomes configurados foram encontrados em cinco endereços de rede naquele momento — não que existam cinco linhas cadastradas.

### Nova identidade Tor

O botão **Nova identidade Tor** solicita um novo circuito para as próximas conexões. Conexões que já estão abertas não são interrompidas e o endereço de saída pode, eventualmente, continuar igual.

### Bandeja do sistema

Clique com o botão direito no ícone para:

- ligar ou desligar a proteção;
- abrir ou reiniciar o Discord;
- solicitar uma nova identidade;
- abrir o painel principal;
- iniciar junto com o Windows;
- sair completamente.

Fechar a janela principal mantém o aplicativo funcionando na bandeja. Para encerrá-lo, escolha **Sair**.

## Privacidade

O Discord Tor Router observa apenas as informações necessárias para reconhecer o aplicativo, o endereço e a porta de destino. Ele não descriptografa conexões, não lê mensagens e não coleta credenciais ou tokens.

Configurações, logs e dados do Tor ficam somente no computador, em `%LocalAppData%\DiscordTorRouter`.

## Dúvidas frequentes

<details>
<summary><strong>Por que o Windows pede permissão de administrador?</strong></summary>
<br>
Porque o roteamento seletivo precisa acessar o sistema de rede do Windows. Sem essa permissão, a proteção não pode ser criada.
</details>

<details>
<summary><strong>O navegador e outros aplicativos também passam pelo Tor?</strong></summary>
<br>
Não. A proteção considera simultaneamente o processo do Discord e os destinos cadastrados.
</details>

<details>
<summary><strong>O que acontece se o Tor cair?</strong></summary>
<br>
As conexões cadastradas são bloqueadas em vez de seguirem pela internet direta. Desligar manualmente a proteção restaura o caminho normal.
</details>

<details>
<summary><strong>O número de destinos protegidos pode mudar?</strong></summary>
<br>
Sim. Serviços como o Discord podem associar o mesmo nome a vários endereços e alterar essa lista ao longo do tempo. O aplicativo atualiza a resolução periodicamente.
</details>

<details>
<summary><strong>Como desinstalar?</strong></summary>
<br>
Abra <strong>Configurações do Windows → Aplicativos → Aplicativos instalados</strong>, procure por Discord Tor Router e escolha <strong>Desinstalar</strong>.
</details>

## Para desenvolvedores

<details>
<summary>Compilar, testar e criar o instalador</summary>
<br>

Requisitos de desenvolvimento:

- SDK .NET 10;
- Visual Studio com desenvolvimento para Windows;
- Inno Setup 6 para gerar o instalador.

```powershell
dotnet restore DiscordTorRouter.slnx
dotnet build DiscordTorRouter.slnx -c Release -p:Platform=x64
dotnet test DiscordTorRouter.slnx -c Release -p:Platform=x64 --no-build
./installer/build-installer.ps1
```

O instalador será criado em `installer/output`.

</details>

## Licença e créditos

O código do Discord Tor Router está disponível sob a [Licença MIT](LICENSE). Tor, WinDivert e demais dependências mantêm suas próprias licenças, descritas em [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

Este é um projeto independente, sem vínculo oficial com Discord Inc. ou The Tor Project.
