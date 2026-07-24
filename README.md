# Trading Monitor

Servicio .NET 9 para monitorear mercado cripto, hacer analisis tecnico multi-temporal, revisar noticias/reportes configurables, pedir un resumen opcional a OpenAI y emitir propuestas de entrada/salida por consola, correo o Telegram.

Este proyecto no ejecuta ordenes reales. Solo genera propuestas para que una persona las revise antes de operar.

## Que hace

- Consulta velas publicas con proveedores encadenados: Binance, Binance US, Coinbase Exchange y Kraken.
- Calcula EMA 9/20/50/200, RSI, MACD, Bollinger Bands, ATR, ADX, VWAP, volumen relativo, soporte y resistencia.
- Evalua coincidencia multi-temporal para LONG o SHORT.
- Lee noticias RSS, reportes macro/regulatorios configurables y clasifica sentimiento basico por palabras clave.
- Usa OpenAI como analista de investigacion opcional para resumir contexto reciente.
- Genera una propuesta con score, zona de entrada, ganancia objetivo, ganancia extra, perdida maxima, razones, riesgos y vigencia.
- Convierte cada propuesta en una instruccion operativa: entrar ahora, vigilar, no entrar, salir con ganancia, salir por perdida maxima o descartar por vencimiento.
- Remarca solo las oportunidades de alta conviccion: score alto, varias temporalidades, riesgo controlado y poco ruido de riesgos. Ninguna senal se presenta como garantia.
- Calcula cuanto podrias ganar o perder si pusieras el monto configurado en cada oportunidad.
- Lanza alertas de salida cuando una oportunidad toca ganancia objetivo, ganancia extra, perdida maxima o vence.
- Evita mandar senales repetidas dentro de una ventana configurable.
- Guarda historial, salidas, salud de fuentes e investigacion en SQL Server local: base `TradingMarket`.
- Registra errores y eventos con Serilog en `logs/`.
- Incluye dashboard web con reportes de trader, fuentes monitoreadas, noticias capturadas, PnL realizado y PnL hipotetico por monto.

Si una fuente falla, el servicio no se apaga: registra el fallo en `data_sources` y sigue con los demas proveedores disponibles.

## Ejecutar

```powershell
dotnet run --project src/Trading.Monitor.Worker
```

## Abrir dashboard web

```powershell
dotnet run --project src/Trading.Monitor.Web --urls http://localhost:5088
```

Luego abre:

```text
http://localhost:5088
```

## Ejecutar con Docker

La forma mas comoda de correr todo junto es Docker Compose. Esto levanta:

- `trading-monitor-web`: dashboard web en `http://localhost:5088`.
- `trading-monitor-worker`: monitor automatico 24/7.
- `trading-monitor-sqlserver`: SQL Server local para SSMS en `localhost,14333`.
- `./logs/web` y `./logs/worker`: registros Serilog.

Primero puedes crear tu archivo de variables:

```powershell
Copy-Item .env.example .env
```

Tu llave real de OpenAI debe vivir en `.env.local` como `OPENAI_API_KEY=...`. Ese archivo esta ignorado por Git y Docker Compose lo pasa al contenedor del worker sin copiarlo dentro de la imagen.

Para SSMS con Docker:

```text
Servidor: localhost,14333
Autenticacion: SQL Server Authentication
Usuario: sa
Password: valor de SQLSERVER_SA_PASSWORD
Base: TradingMarket
```

Para SSMS con tu SQL Server local instalado:

```text
Servidor: localhost
Autenticacion: Windows Authentication
Base: TradingMarket
```

Luego construye y levanta los servicios:

```powershell
docker compose up --build -d
```

Abre el dashboard:

```text
http://localhost:5088
```

Paginas principales:

- `http://localhost:5088/`: resumen ejecutivo.
- `http://localhost:5088/acciones`: entradas, salidas, ganancia objetivo, perdida maxima y calculos por monto.
- `http://localhost:5088/api/operaciones-vivas?capital=1000`: JSON del tablero vivo.
- `http://localhost:5088/api/grafico-vivo?symbol=BTCUSDT&interval=1m&capital=1000`: velas 1m y niveles de entrada, ganancia y perdida maxima.
- `http://localhost:5088/api/exchange/status`: estado seguro de integracion con exchange.
- `http://localhost:5088/reportes`: reportes por simbolo, dia, win rate y PnL.
- `http://localhost:5088/conexiones`: salud de exchanges, noticias, reportes e IA.
- `http://localhost:5088/logs`: visor local de logs Serilog.

Ver logs:

```powershell
docker compose logs -f worker
docker compose logs -f web
```

Apagar:

```powershell
docker compose down
```

Reconstruir despues de cambios:

```powershell
docker compose build --no-cache
docker compose up -d
```

La base queda en SQL Server:

```text
Database: TradingMarket
SSMS Docker: localhost,14333
SSMS local Windows Auth: localhost
```

Para una prueba de una sola pasada:

```powershell
$env:DOTNET_ENVIRONMENT="Development"
dotnet run --project src/Trading.Monitor.Worker
```

## Configuracion principal

Edita `src/Trading.Monitor.Worker/appsettings.json` o crea `src/Trading.Monitor.Worker/appsettings.Local.json` con tus valores privados.

Campos importantes:

- `TradingMonitor:Symbols`: pares a monitorear, por ejemplo `BTCUSDT`, `ETHUSDT`.
- `TradingMonitor:Intervals`: temporalidades a evaluar.
- `TradingMonitor:MinimumScore`: puntuacion minima para lanzar alerta.
- `TradingMonitor:EvaluationIntervalSeconds`: frecuencia de escaneo.
- `MarketSources:*Enabled`: activa o desactiva Binance, Binance US, Coinbase y Kraken.
- `MarketSources:*BaseUrl`: URLs base de cada proveedor de mercado.
- `MarketSources:TimeoutSeconds`: limite por solicitud de mercado.
- `Reporting:DefaultCapital`: monto X usado para calcular cuanto pudiste ganar o perder.
- `Reporting:EstimatedFeePercentPerSide`: comision estimada por lado.
- `Database:ConnectionString`: conexion SQL Server usada por Entity Framework.
- `News:Feeds`: fuentes RSS, reportes y recursos a revisar.
- `News:FearGreedEnabled`: agrega el indice publico Fear & Greed como contexto de sentimiento.
- `News:CryptoPanicEnabled`: activa CryptoPanic si configuras `CRYPTOPANIC_AUTH_TOKEN`.
- `News:CryptoPanicAuthTokenEnvironmentVariable`: nombre de la variable que contiene el token de CryptoPanic.
- `OpenAi:Enabled`: activa o desactiva el resumen de investigacion con OpenAI.
- `OpenAi:Model`: modelo usado para el resumen de investigacion.
- `Notifications:Email`: SMTP para correo.
- `Notifications:Telegram`: bot token y chat id.
- `ExchangeExecution:Mode`: `Paper` por defecto. `Live` solo debe usarse con llaves sin retiro, limites y `AllowLiveOrders=true`.
- `ExchangeExecution:MaxCapitalPerTrade`: limite por operacion para la fase live.
- `ExchangeExecution:DailyLossLimit`: limite diario de perdida.

Tambien puedes usar variables de entorno:

```powershell
$env:Notifications__Email__Enabled="true"
$env:Notifications__Email__Host="smtp.gmail.com"
$env:Notifications__Email__Port="587"
$env:Notifications__Email__UserName="tu-correo@gmail.com"
$env:Notifications__Email__Password="tu-app-password"
$env:Notifications__Email__From="tu-correo@gmail.com"
$env:Notifications__Email__To="destino@correo.com"
```

## Pruebas

```powershell
dotnet test
```

## Siguientes mejoras naturales

- WebSocket de Binance y Coinbase para menor latencia.
- Calendario economico y eventos macro con proveedor dedicado.
- Backtesting por estrategia antes de subir el score minimo.
- Filtros por estrategia, sesion, fuente y regimen de mercado.
- Paper trading con estadisticas de acierto, drawdown y profit factor.
- Conectores adicionales para acciones, opciones o futuros.
