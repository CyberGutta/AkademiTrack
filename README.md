# AkademiTrack - STU Tidsregistrering

*Automatisk oppmøteregistrering for STU-økter på Akademiet*

## 🇳🇴 Norsk

<img width="738" height="649" alt="AkademiTrack Interface" src="https://github.com/user-attachments/assets/2abb0222-9737-48da-bdb8-b82df2c0c32d" />

### Om Programmet

AkademiTrack er et automatiseringsverktøy som hjelper studenter ved Akademiet med å registrere oppmøte for STU (Selvstendige Terminoppgaver) økter automatisk. Programmet overvåker timeplandata og registrerer oppmøte når registreringsvinduene åpner seg.

### Hovedfunksjoner

- **Automatisk innlogging**: Bruker nettleser for sikker autentisering mot iSkole systemet
- **Intelligent overvåking**: Overvåker kun aktuelle STU-økter for gjeldende dag
- **Optimalisert ytelse**: Gjør kun én API-forespørsel per dag for å hente timeplandata
- **Sanntids registrering**: Registrerer oppmøte automatisk når registreringsvinduene åpner
- **Detaljert logging**: Omfattende loggføring med mulighet for detaljerte debug-meldinger
- **System notifikasjoner**: Diskrete overlay-notifikasjoner for viktige hendelser
- **Innstillinger**: Konfigurerbar logging og programinnstillinger

### Hvordan Det Fungerer

1. **Start automatisering**: Klikk på "Start Automatisering" knappen
2. **Første gangs innlogging**: Nettleser åpnes for sikker pålogging til iSkole
3. **Cookie lagring**: Autentisering lagres sikkert for fremtidige økter
4. **Overvåking starter**: Programmet henter dagens STU-økter og overvåker registreringsvinduene
5. **Automatisk registrering**: Når et registreringsvindu åpner, registreres oppmøte automatisk
6. **Fullføring**: Programmet stopper når alle økter for dagen er behandlet

### Systemkrav

- **Operativsystem**: Windows, macOS
- **Internett**: Stabil internettforbindelse
- **Tilgang**: Gyldig iSkole konto ved Akademiet

### Installasjon og Bruk

1. Last ned den nyeste versjonen fra releases
2. Pakk ut filene til ønsket mappe
3. Kjør `AkademiTrack.exe` (Windows) eller tilsvarende for ditt operativsystem
4. Klikk "Start Automatisering" og følg instruksjonene for innlogging
5. La programmet kjøre i bakgrunnen - det vil håndtere alt automatisk

### Sikkerhet og Personvern

- **Lokalt lagret data**: Alle cookies og data lagres kun lokalt på din maskin
- **Ingen datainnsamling**: Programmet sender ikke personopplysninger til utvikleren
- **Sikker autentisering**: Bruker offisiell iSkole innloggingsmetode
- **Automatisk opprydding**: Gamle logger og data slettes automatisk

### Feilsøking

**Problem: Automatisering stopper uventet**
- Løsning: Start på nytt for å få nye autentisering-cookies


**Problem: Ingen STU-økter funnet**
- Løsning: Kontroller at du har STU-økter i timeplanen din for i dag

### Support

For teknisk support eller spørsmål, opprett en issue i GitHub repository eller kontakt utvikleren direkte.

### Versjon

Gjeldende versjon: 1.0.0.0

---

## 🇺🇸 English Version Below

---

# AkademiTrack - STU Time Registration

*Automated attendance registration for STU sessions at Akademiet*

## 🇺🇸 English

### About the Program

AkademiTrack is an automation tool that helps students at Akademiet (a Norwegian school) automatically register attendance for STU (Independent Term Projects) sessions. The program monitors schedule data and registers attendance when registration windows open.

### Key Features

- **Automatic login**: Uses browser for secure authentication against the iSkole system
- **Intelligent monitoring**: Only monitors relevant STU sessions for the current day
- **Optimized performance**: Makes only one API request per day to fetch schedule data
- **Real-time registration**: Automatically registers attendance when registration windows open
- **Detailed logging**: Comprehensive logging with option for detailed debug messages
- **System notifications**: Discrete overlay notifications for important events
- **Settings**: Configurable logging and program settings

### How It Works

1. **Start automation**: Click the "Start Automatisering" (Start Automation) button
2. **First-time login**: Browser opens for secure login to iSkole
3. **Cookie storage**: Authentication is stored securely for future sessions
4. **Monitoring begins**: Program fetches today's STU sessions and monitors registration windows
5. **Automatic registration**: When a registration window opens, attendance is automatically registered
6. **Completion**: Program stops when all sessions for the day have been handled

### System Requirements

- **Operating System**: Windows, macOS
- **Internet**: Stable internet connection
- **Access**: Valid iSkole account at Akademiet

### Installation and Usage

1. Download the latest version from releases
2. Extract files to desired folder
3. Run `AkademiTrack.exe` (Windows) or equivalent for your operating system
4. Click "Start Automatisering" and follow the login instructions
5. Let the program run in the background - it will handle everything automatically

### Security and Privacy

- **Locally stored data**: All cookies and data are stored only locally on your machine
- **No data collection**: Program does not send personal information to the developer
- **Secure authentication**: Uses official iSkole login method
- **Automatic cleanup**: Old logs and data are automatically deleted

### Troubleshooting

**Problem: Automation stops unexpectedly**
- Solution: Restart to get new authentication cookies

**Problem: No STU sessions found**
- Solution: Check that you have STU sessions in your schedule for today

### Support

For technical support or questions, create an issue in the GitHub repository or contact the developer directly.

### Version

Current version: 1.0.0.0

---

## License

This project is provided as-is for educational and personal use. Please ensure compliance with your school's policies regarding automated tools.

## Disclaimer

This tool is designed to assist students with legitimate attendance registration. Users are responsible for ensuring their use complies with school policies and academic integrity standards.
