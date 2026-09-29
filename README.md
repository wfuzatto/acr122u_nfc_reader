# ACR122U NFC Reader

Aplicativo Windows para inspecionar, ler e editar cartões NFC/RFID compatíveis com o leitor ACS ACR122U diretamente via PC/SC.

## Objetivo

Substituir as ferramentas antigas/proprietárias de teste do ACR122U por um aplicativo próprio, simples e auditável.

O programa usa diretamente o subsistema Smart Card do Windows (winscard.dll). Não usa DLL de terceiros, não depende de software da ACS e não possui pacotes NuGet em runtime.

## Recursos

- Detecta leitores PC/SC instalados e seleciona o ACR122U.
- Detecta inserção/remoção do cartão.
- Exibe nome do leitor, UID HEX/decimal, ATR completo, protocolo PC/SC, estado, firmware e tipo provável do cartão.
- Leitura de MIFARE Classic 1K, Classic 4K e MIFARE Ultralight.
- Leitura manual por bloco/página.
- Autenticação MIFARE Classic com Key A / Key B.
- Chaves informadas pelo operador e conjunto pequeno de chaves padrão conhecidas, opcional.
- Dump com setor, bloco/página, HEX, ASCII, autenticação utilizada e access bits/GPB do trailer.
- Editor de bloco/página.
- Proteção contra gravação acidental do bloco fabricante/UID e de Sector Trailer.
- Console APDU manual.
- Exportação do dump para JSON.
- Log de operações e respostas APDU.

## Limitações importantes

As chaves secretas de um MIFARE Classic não podem ser simplesmente lidas do cartão. O programa consegue ler um setor somente quando a autenticação é aceita por uma chave conhecida/informada. Este projeto não implementa quebra de chaves, brute force ou ataques criptográficos.

Nem todo cartão NFC é uma memória simples. Cartões ISO 14443-4, DESFire, cartões bancários e aplicações proprietárias usam protocolos e comandos próprios. Para esses casos, a aba APDU permite comunicação manual, mas o programa não tenta contornar mecanismos de segurança.

## Dependências

O executável publicado é self-contained:

- .NET 8 Runtime: incluído no EXE.
- WinForms: incluído no EXE.
- Bibliotecas do projeto: incluídas no EXE.
- PC/SC: usa winscard.dll, componente nativo do Windows.

O driver do dispositivo USB é responsabilidade do Windows/driver do fabricante. Em Windows 10/11, o ACR122U normalmente aparece como leitor Smart Card/CCID. Se ele não aparecer em Gerenciador de Dispositivos ou no aplicativo, instale o driver PC/SC oficial da ACS.

## Compilar

No Windows com .NET 8 SDK:

~~~powershell
.\build.ps1
~~~

Saída:

~~~text
dist\ACR122U.NFC.Reader.exe
dist\acr122u_nfc_reader-win-x64.zip
~~~

## GitHub Actions

Cada push na branch main executa a compilação em Windows e publica um artifact chamado acr122u_nfc_reader-win-x64.

Também é possível executar manualmente em Actions > Build Windows EXE > Run workflow.

## Estrutura

~~~text
src/Acr122uNfcReader/
  Program.cs
  MainForm.cs
  Pcsc/
    NativeMethods.cs
    PcscReader.cs
  Nfc/
    CardCommands.cs
    CardAnalyzer.cs
.github/workflows/
  build-windows.yml
build.ps1
~~~

## Referências técnicas

- ACS ACR122U Application Programming Interface, v2.x
- Microsoft Windows Smart Card API / WinSCard

Use somente em cartões, tags e sistemas para os quais você tenha autorização.
