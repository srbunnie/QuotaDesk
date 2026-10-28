# Roteiro

**Português** · [English](ROADMAP.en.md)

Hoje o QuotaDesk roda só no Windows. O objetivo das próximas versões é levar o mesmo escritório e os mesmos avisos para o **Linux** e o **macOS**, sem perder nada do que já funciona no Windows.

## Por que ainda é só Windows

O app nasceu como um app de bandeja em WinForms. Vários pedaços dependem do Windows:

| Peça | Hoje (Windows) | Linux | macOS |
|---|---|---|---|
| Janelas e desenho | WinForms + GDI+ | Avalonia + Skia | Avalonia + Skia |
| Ícone de status | `NotifyIcon` na bandeja | ícone na bandeja (StatusNotifierItem) | ícone na barra de menus |
| Balão e barra na borda da tela | janelas em camadas do Win32 | janelas transparentes do Avalonia | janelas transparentes do Avalonia |
| Logins guardados | DPAPI | Secret Service (libsecret) | Keychain |
| Pasta de dados | `%LOCALAPPDATA%\AIUsageMonitor` | `~/.local/share/QuotaDesk` | `~/Library/Application Support/QuotaDesk` |
| Uma instância só | mutex nomeado | socket local | socket local |
| Iniciar com o sistema | chave `Run` do registro | `~/.config/autostart/*.desktop` | LaunchAgent |
| Notificações | balão da bandeja | libnotify | Central de Notificações |
| statusLine do Claude Code | `claude-statusline.ps1` | script `sh` | script `sh` |
| Instalação | `install.ps1` e zip | AppImage e `.deb` | `.dmg` assinado e notarizado |

As leituras das IAs (Codex, Claude Code, Antigravity, Magnific, OpenRouter e Figma) já são quase todas independentes do sistema. Elas mudam pouco, só os caminhos e os nomes de processo.

## Etapas

**1. Separar o núcleo**
- Mover modelos, provedores, previsão de esgotamento e configurações para uma biblioteca `QuotaDesk.Core`, sem WinForms.
- Pôr atrás de interfaces o que depende do sistema: pasta de dados, logins guardados, instância única, inicialização com o sistema e notificações.
- O app do Windows continua funcionando igual. Os autotestes passam a rodar no núcleo também no Linux, pelo CI.

**2. Trocar o desenho**
- Portar o escritório, os cards, os balões e as barras de GDI+ para Skia, que funciona nos três sistemas.
- Comparar as capturas de `--preview` antes e depois para garantir o mesmo visual.

**3. Linux**
- Interface em Avalonia, com ícone na bandeja, painel e barra na borda da tela.
- Logins no libsecret, statusLine do Claude Code em `sh` e pacotes AppImage e `.deb`.
- Testes em GNOME e KDE.

**4. macOS**
- Ícone na barra de menus, painel e logins no Keychain.
- App assinado e notarizado, para Intel e Apple Silicon.

**5. Telinha USB nos três sistemas**
- A porta serial já usa `System.IO.Ports`, que funciona fora do Windows. Falta testar as permissões de acesso à porta no Linux e no macOS.

## Como ajudar

- Abra uma issue dizendo qual sistema e qual ambiente de desktop você usa. Isso ajuda a priorizar.
- A etapa 1 não depende de Mac nem de Linux: dá para contribuir só com Windows.
- Siga as regras do [AGENTS.md](AGENTS.md) e do [SECURITY.md](SECURITY.md). As regras de privacidade valem igual em todos os sistemas.
