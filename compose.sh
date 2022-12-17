if [ "$ver" == "" ]; then
ver=1.0.0
fi

docker build -t "scholtz2/algorand-amm-staking-bot:$ver-stable" -f AMMStakingBot/Dockerfile  ./ || error_code=$?
error_code_int=$(($error_code + 0))
if [ $error_code_int -ne 0 ]; then
    echo "docker build failed";
	exit 1;
fi
docker push "scholtz2/algorand-amm-staking-bot:$ver-stable" || error_code=$?
error_code_int=$(($error_code + 0))
if [ $error_code_int -ne 0 ]; then
    echo "docker push failed";
	exit 1;
fi
echo "Image: scholtz2/algorand-amm-staking-bot:$ver-stable"
