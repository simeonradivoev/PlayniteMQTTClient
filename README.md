# MQTT Client Extension for Playnite
This extension posts different topics to MQTT server. They include:
* Selected Game
	* Cover Image (optional)
	* Dominant Cover Color (optional)
* Playing Game
	* Cover Image (optional)
	* Background Image (optional)
	* Icon
	* Dominant Cover Color
* Active View
* Installed Game
* Uninstalled Game

## Connection
It automatically connects to the configured server on every playnite app launch. To disconnect and connect use the side menu icon (that also turns active if the client is connected).

## Home Assistant
Support for home assistant discovery is built in. All attributes and entities and devices should be discoverable by home assistant and it's MQTT integration

## Security
Currently the only option supported is logging it through username and password.

## Notes
This was made mainly for personal use so I probably won't include localization, also if you know how to work with MQTT you probably know English.

As of now re-connection retrying isn't implemented yet.