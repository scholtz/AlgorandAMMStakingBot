ver=1.0.0
docker build -t "scholtz2/tinyman-staking-bot:$ver-stable" -f TinyManStakingBot/Dockerfile  ./
docker push "scholtz2/tinyman-staking-bot:$ver-stable"
echo "Image: scholtz2/tinyman-staking-bot:$ver-stable"
