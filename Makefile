# Build, install and package KarmaLocking.
#
#   make build       Build the plugin (bin/Release)
#   make install     Build and copy into an r2modman profile
#   make uninstall   Remove from that profile
#   make package     Build the Thunderstore zip into dist/ (see package.sh)
#   make icons       Regenerate icon.png and assets/lock.png (needs Pillow)
#   make test        Scripted in-game test: launches the game with a throwaway profile (see tests/run_ingame_test.sh)
#   make clean       Remove build output
#
# Paths can be overridden: make install PROFILE_DIR=/path/to/r2modman/profile
# The profile's MMHOOK_RoR2.dll must match the current game version (launch it once after a game update).
RORDIR      ?= $(HOME)/.local/share/Steam/steamapps/common/Risk of Rain 2
PROFILE_DIR ?= $(HOME)/.config/r2modmanPlus-local/RiskOfRain2/profiles/Hallowed Ready
CONFIG      ?= Release

NAMESPACE := $(shell bash -c 'source ./thunderstore.conf && echo $$NAMESPACE')
NAME      := $(shell bash -c 'source ./thunderstore.conf && echo $$NAME')
VERSION   := $(shell bash -c 'source ./thunderstore.conf && echo $$VERSION')

DLL         := bin/$(CONFIG)/$(NAME).dll
INSTALL_DIR := $(PROFILE_DIR)/BepInEx/plugins/$(NAMESPACE)-$(NAME)

.PHONY: all build install uninstall package icons test clean

all: build

build:
	dotnet build -c $(CONFIG) -p:Version=$(VERSION) -p:RoR2Dir="$(RORDIR)" -p:ProfileDir="$(PROFILE_DIR)"

install: build
	mkdir -p "$(INSTALL_DIR)"
	cp "$(DLL)" icon.png "$(INSTALL_DIR)/"
	@echo "Installed to $(INSTALL_DIR)"

uninstall:
	rm -rf "$(INSTALL_DIR)"

package: build
	./package.sh "$(DLL)"

icons:
	python3 tools/make_icons.py

test:
	tests/run_ingame_test.sh "$(PROFILE_DIR)"

clean:
	dotnet clean -c $(CONFIG) >/dev/null
	rm -rf bin obj dist tests/.profile tests/TestDriver/bin tests/TestDriver/obj
