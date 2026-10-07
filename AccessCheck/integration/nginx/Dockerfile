FROM alpine:3

RUN apk add --no-cache nginx nginx-mod-http-dav-ext

COPY nginx.conf /etc/nginx/nginx.conf

CMD ["nginx", "-g", "daemon off;"]
