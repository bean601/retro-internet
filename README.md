# Retro Internet

Ever wondered what the internet was like in 1999? Wonder no more!

This is a .NET Core web application that dynamically rewrites archive.org HTML pages, giving you that nostalgic dial-up browsing experience.

It is designed to run on a Raspberry Pi (any version) and by default blinks LEDs on GPIO pins 27 (success and activity) and 17 (errors). It can run anywhere though.

## Overview

This project runs as a .NET Core web application under nginx on port 5000, with DNS manipulation handled by dnsmasq for a contained retro browsing environment.

## System Components

- .NET Core web application
- nginx web server
- dnsmasq for DNS manipulation

## Configuration

### DNSMasq Configuration

Create or modify `/etc/dnsmasq.conf`:

```conf
# Interface configuration
interface=eth0      # The interface dnsmasq should listen on

# DHCP configuration
dhcp-range=192.168.2.50,192.168.2.150,255.255.255.0,72h  # IP range and lease time

# DNS configuration
address=/#/192.168.2.1  # Resolve all domains to 192.168.2.1
no-resolv            # Prevent using resolv file for upstream DNS servers

# Logging
log-queries
log-facility=/var/log/dnsmasq.log
```

### Nginx Configuration

Create or modify `/etc/nginx/nginx.conf`:

```nginx
user www-data;
worker_processes auto;
pid /run/nginx.pid;
error_log /var/log/nginx/error.log;

include /etc/nginx/modules-enabled/*.conf;

events {
    worker_connections 768;
}

http {
    # Basic Settings
    sendfile on;
    tcp_nopush on;
    types_hash_max_size 2048;
    include /etc/nginx/mime.types;
    default_type application/octet-stream;

    # SSL Settings
    ssl_protocols TLSv1 TLSv1.1 TLSv1.2 TLSv1.3;
    ssl_prefer_server_ciphers on;

    # Logging
    access_log /var/log/nginx/access.log;

    # Gzip Settings
    gzip on;

    # Virtual Host Configs
    include /etc/nginx/conf.d/*.conf;
    include /etc/nginx/sites-enabled/*;
}

# Stream Configuration for HTTPS
stream {
    upstream dotnet_app_https {
        server localhost:5000;
    }

    server {
        listen 443;
        listen [::]:443;
        ssl_preread on;
        proxy_pass dotnet_app_https;
        proxy_connect_timeout 1s;
        proxy_timeout 3s;
    }
}
```

## Setup Instructions

1. Install required components:
   - .NET Core
   - nginx
   - dnsmasq

2. Place the configuration files in their respective locations:
   - dnsmasq config: `/etc/dnsmasq.conf`
   - nginx config: `/etc/nginx/nginx.conf`

3. Start the services:
   ```bash
   sudo systemctl start dnsmasq
   sudo systemctl start nginx
   dotnet run  # In the application directory
   ```
