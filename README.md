# G321 Studio

Aplicativo independente e não oficial para uso com o headset Logitech G G321 LIGHTSPEED. Oferece equalizador de 10 bandas com presets, retorno de voz com volume ajustável e leitura da porcentagem de bateria. O projeto é mantido sem afiliação, patrocínio ou endosso da Logitech. [Headset compatível](https://www.logitechg.com/pt-br/shop/p/g321.981-001562)

## Instalação

Requer Windows 10 (versão 2004 ou posterior) ou Windows 11. Abra **G321 Studio Setup.exe** na conta que vai usar o aplicativo. O arquivo inclui as versões x64, x86 e ARM64, o ambiente .NET e o instalador oficial do Equalizer APO. A instalação pode ser feita sem internet.

Mantenha selecionada a opção **Iniciar o G321 Studio na bandeja ao entrar no Windows** se quiser iniciar o app junto com o Windows. Para atualizar ou desinstalar o G321 Studio, use **Sair** na bandeja antes de abrir o instalador ou o desinstalador.

## Equalizador

O EQ direto usa o Equalizer APO na saída de reprodução **G321 LIGHTSPEED**. Se o Equalizer APO ainda estiver ausente, marque **Instalar o Equalizer APO** na última tela do Setup. O Windows pede permissão de administrador para essa etapa. Depois da autorização, o Setup extrai o instalador do APO em uma pasta temporária protegida e confere seu SHA-256 antes de abri-lo. O instalador oficial desativa o modo protegido do mecanismo de áudio para carregar o efeito. Essa configuração vale para o sistema todo. Para instalar sem internet, desmarque a procura automática por atualizações no instalador do Equalizer APO.

Conecte o receptor, ligue o fone em LIGHTSPEED e habilite o APO somente na saída de reprodução do G321 pelo **Equalizer APO Device Selector**. Se o APO já estiver instalado, abra o Device Selector pelo menu Iniciar. Reinicie se o instalador solicitar.

Mantenha os aprimoramentos de áudio habilitados e selecione o G321 como saída do Windows ou do jogo. Selecione um preset, arraste os pontos da curva ou use os controles circulares para ajustar as bandas. Ao editar uma banda ou o pré-amplificador, o app armazena os ajustes em **Personalizado**. O EQ também se aplica ao áudio do retorno de voz nessa saída. [Equalizer APO](https://sourceforge.net/projects/equalizerapo/)

## Retorno de voz

Escolha a saída e o microfone do G321. Ao ligar a chave do retorno, confirme o par de dispositivos e ajuste o volume. Em LIGHTSPEED, ambos precisam pertencer ao mesmo receptor USB. A confirmação vale na sessão atual até você trocar de dispositivo ou o app detectar que o par foi desconectado. A opção **Sair** na bandeja encerra o retorno.

## Bandeja e ativação automática

O G321 Studio inicia na bandeja do Windows. Para mostrar a janela, clique duas vezes no ícone ou selecione **Abrir G321 Studio** no menu da bandeja. O **X** e o botão de minimizar recolhem a janela. O EQ e o retorno de voz continuam funcionando se estiverem ligados. Para encerrar o aplicativo, use **Sair** no menu da bandeja.

Com o APO configurado, o Studio liga o EQ com o preset **Plano** quando o G321 responde em LIGHTSPEED. Usar a chave do EQ pausa a ativação automática até a próxima abertura do app.

## Bateria

Com o G321 em LIGHTSPEED, o app lê a porcentagem de bateria informada pelo receptor USB. Se essa leitura estiver indisponível e o G321 estiver conectado por Bluetooth, o app procura o nível informado pelo Windows ou pelo serviço Bluetooth de bateria.

A leitura é atualizada automaticamente e pode ser solicitada em **Atualizar dispositivos**. Quando a porcentagem está indisponível, o app exibe um traço.

## Privacidade

O retorno de voz usa o microfone após sua confirmação e mantém o áudio em memória para reprodução local. O G321 Studio não grava nem transmite esse áudio. O app guarda as preferências de EQ, o volume do retorno e os identificadores dos dispositivos de áudio selecionados em `%LOCALAPPDATA%\G321 Studio\settings.json`.

Ao relatar um problema, oculte nomes de usuário, caminhos pessoais, números de série e identificadores de dispositivos antes de compartilhar registros, capturas de tela ou diagnósticos.

## Compilar

Use o SDK .NET 10 para gerar o executável x64 em `dist`.

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o dist
```

Para gerar o instalador, use PowerShell 7, o SDK .NET 10, o compilador Inno Setup 6.7.3 ou posterior e os quatro arquivos do Equalizer APO 1.4.2. O script confere os arquivos por SHA-256 e gera `dist/G321 Studio Setup.exe`. [Arquivos oficiais do Equalizer APO 1.4.2](https://sourceforge.net/projects/equalizerapo/files/1.4.2/)

```powershell
./installer/Build-OfflineSetup.ps1 -ApoDirectory 'C:\build\EqualizerAPO-1.4.2' -InnoCompiler 'C:\build\InnoSetup\ISCC.exe'
```

## Licenças e marcas

A licença do código do G321 Studio ainda não foi definida. Os componentes de terceiros seguem suas próprias licenças.

- O Equalizer APO 1.4.2 é um componente separado, distribuído sob GPLv2. O instalador inclui o arquivo oficial com o código-fonte correspondente, `EqualizerAPO-src-1.4.2.zip`, na pasta **Licenças** da instalação. [Projeto Equalizer APO](https://sourceforge.net/projects/equalizerapo/)
- O NAudio usa a licença MIT. O arquivo de licença está no repositório e também é instalado na pasta **Licenças**, junto com os avisos do .NET e do Windows Desktop. [Licença do NAudio](installer/NAudio-LICENSE.txt)

Logitech, Logitech G e LIGHTSPEED são marcas comerciais ou registradas da Logitech Europe S.A. ou de suas afiliadas nos Estados Unidos ou em outros países. [Diretrizes de marcas da Logitech](https://www.logitech.com/en-us/tos/trademark-guidelines)
