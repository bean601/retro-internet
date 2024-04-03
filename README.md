# retro-internet
Dynamically rewrites archive.org html pages

.net core web app running under nginx port 5000


dnsmasq - /etc/dnsmasq.conf


interface=eth0      # The interface dnsmasq should listen on.
dhcp-range=192.168.2.50,192.168.2.150,255.255.255.0,72h  # IP range and lease time
address=/#/192.168.2.1  # Resolve all domains to 192.168.2.1

# Prevent dnsmasq from using any resolv file for upstream DNS servers
no-resolv

# Enable logging
log-queries
log-facility=/var/log/dnsmasq.log

/etc/nginx/nginx.conf - 

user www-data;
worker_processes auto;
pid /run/nginx.pid;
error_log /var/log/nginx/error.log;
#load_module modules/ngx_stream_module.so;
include /etc/nginx/modules-enabled/*.conf;

events {
        worker_connections 768;
        # multi_accept on;
}

http {

        ##
        # Basic Settings
        ##

        sendfile on;
        tcp_nopush on;
        types_hash_max_size 2048;
        # server_tokens off;

        # server_names_hash_bucket_size 64;
        # server_name_in_redirect off;

        include /etc/nginx/mime.types;
        default_type application/octet-stream;

        ##
        # SSL Settings
        ##

        ssl_protocols TLSv1 TLSv1.1 TLSv1.2 TLSv1.3; # Dropping SSLv3, ref: POODLE
        ssl_prefer_server_ciphers on;

        ##
        # Logging Settings
        ##

        access_log /var/log/nginx/access.log;

        ##
        # Gzip Settings
        ##

        gzip on;

        # gzip_vary on;
        # gzip_proxied any;
        # gzip_comp_level 6;
        # gzip_buffers 16 8k;
        # gzip_http_version 1.1;
        # gzip_types text/plain text/css application/json application/javascript text/xml application/xml application/xml+rss text/javascript;

        ##
        # Virtual Host Configs
        ##

        include /etc/nginx/conf.d/*.conf;
        include /etc/nginx/sites-enabled/*;
}


#mail {
#       # See sample authentication script at:
#       # http://wiki.nginx.org/ImapAuthenticateWithApachePhpScript
#
#       # auth_http localhost/auth.php;
#       # pop3_capabilities "TOP" "USER";
#       # imap_capabilities "IMAP4rev1" "UIDPLUS";
#
#       server {
#               listen     localhost:110;
#               protocol   pop3;
#               proxy      on;
#       }
#
#       server {
#               listen     localhost:143;
#               protocol   imap;
#               proxy      on;
#       }
#}


# Stream block for TCP/UDP forwarding
stream {
    # Configuration for forwarding HTTPS traffic
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
