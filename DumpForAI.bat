@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

:: --- CONFIG ---
set "OUTPUT_DIR=_AI_Dump"
set "EXCLUDE_DIRS=bin obj .vs .git node_modules packages _AI_Dump"

echo.
echo ============================================
echo    DUMP PROGETTO PER AI - Avvio...
echo ============================================
echo.

:: --- PULIZIA VECCHIO DUMP ---
if exist "%~dp0%OUTPUT_DIR%" (
    echo [INFO] Rimozione vecchio dump...
    rd /s /q "%~dp0%OUTPUT_DIR%"
)
mkdir "%~dp0%OUTPUT_DIR%"

set "COUNT=0"
set "SKIPPED=0"

:: --- LOOP SU TUTTI I FILE ---
for /r "%~dp0." %%F in (*) do (
    set "SKIP=0"
    set "REL=%%F"
    set "REL=!REL:%~dp0=!"

    :: Escludi cartelle indesiderate
    for %%X in (%EXCLUDE_DIRS%) do (
        echo !REL! | findstr /i /b "%%X\\" >nul 2>&1 && set "SKIP=1"
        echo !REL! | findstr /i "\\%%X\\" >nul 2>&1 && set "SKIP=1"
    )

    :: Escludi il .bat stesso
    if "%%~nxF"=="%~nx0" set "SKIP=1"

    if "!SKIP!"=="0" (
        :: Sostituisci \ con --- nel path relativo
        set "NEWNAME=!REL:\=---!"
        copy "%%F" "%~dp0%OUTPUT_DIR%\!NEWNAME!.txt" >nul 2>&1
        if !errorlevel! equ 0 (
            echo [OK] !REL!
            set /a COUNT+=1
        ) else (
            echo [ERRORE] !REL!
        )
    ) else (
        set /a SKIPPED+=1
    )
)

echo.
echo ============================================
echo    DUMP COMPLETATO!
echo --------------------------------------------
echo    File copiati:  %COUNT%
echo    File esclusi:  %SKIPPED%
echo    Cartella:      %~dp0%OUTPUT_DIR%
echo ============================================
echo.
pause
