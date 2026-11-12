@echo off
REM Shapester: sirve la version web del juego en http://localhost:8765
REM (y en tu red Wi-Fi, para el celular: http://IP-DE-TU-PC:8765)
REM Cerrar esta ventana apaga el servidor.
echo.
echo  Shapester corriendo en:  http://localhost:8765
echo  Desde el celular (mismo Wi-Fi): http://192.168.0.177:8765
echo.
echo  Para probar 2 jugadores: abri 2 VENTANAS del navegador lado a lado
echo  (no 2 pestanas en la misma ventana) y entra al link en cada una.
echo.
start "" http://localhost:8765
python -m http.server 8765 --bind 0.0.0.0 --directory "%~dp0CajaJusta\Builds\Web"
